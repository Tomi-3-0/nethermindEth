using System.Buffers.Binary;
using System.Diagnostics;
using Nethermind.Core.Crypto;
using Nethermind.Db;
using Nethermind.Trie.Pruning;
using Nethermind.Verkle.Tree.Nodes;
using Nethermind.Verkle.Tree.Utils;
using Nethermind.Verkle.Tree.VerkleDb;

namespace Nethermind.Verkle.Tree;

public class VerkleStateStore : IVerkleStore, ISyncTrieStore
{
    /// <summary>
    ///  maximum number of blocks that should be stored in cache (not persisted in db)
    /// </summary>
    private int MaxNumberOfBlocksInCache { get; set; }

    /// <summary>
    /// the blockNumber for with the fullState is persisted in the database.
    /// </summary>
    private long FullStatePersistedBlock { get; set; }
    private long FullStateCacheBlock { get; set; }

    private Pedersen? PersistedStateRoot { get;  set; }

    private StackQueue<(long, VerkleMemoryDb)> BlockCache { get; set; }

    public Pedersen StateRoot { get; private set; }

    public Pedersen RootHash
    {
        get => GetStateRoot();
        set => MoveToStateRoot(value);
    }


    // The underlying key value database
    // We try to avoid fetching from this, and we only store at the end of a batch insert
    private VerkleKeyValueDb Storage { get; }

    private VerkleHistoryStore History { get; }

    private readonly StateRootToBlockMap _stateRootToBlocks;

    public VerkleStateStore(IDbProvider dbProvider, int maxNumberOfBlocksInCache = 128)
    {
        Storage = new VerkleKeyValueDb(dbProvider);
        History = new VerkleHistoryStore(dbProvider);
        _stateRootToBlocks = new StateRootToBlockMap(dbProvider.StateRootToBlocks);
        BlockCache = new StackQueue<(long, VerkleMemoryDb)>(maxNumberOfBlocksInCache);
        MaxNumberOfBlocksInCache = maxNumberOfBlocksInCache;
        InitRootHash();
        StateRoot = GetStateRoot();
        FullStatePersistedBlock = _stateRootToBlocks[StateRoot];
        FullStateCacheBlock = -1;

        // TODO: why should we store using block number - use stateRoot to index everything
        // but i think block number is easy to understand and it maintains a sequence
        if (FullStatePersistedBlock == -2) throw new Exception("StateRoot To BlockNumber Cache Corrupted");
    }

    public ReadOnlyVerkleStateStore AsReadOnly(VerkleMemoryDb keyValueStore)
    {
        return new ReadOnlyVerkleStateStore(this, keyValueStore);
    }

    // This generates and returns a batchForwardDiff, that can be used to move the full state from fromBlock to toBlock.
    // for this fromBlock < toBlock - move forward in time
    public VerkleMemoryDb GetForwardMergedDiff(long fromBlock, long toBlock)
    {
        return History.GetBatchDiff(fromBlock, toBlock).DiffLayer;
    }

    // This generates and returns a batchForwardDiff, that can be used to move the full state from fromBlock to toBlock.
    // for this fromBlock > toBlock - move back in time
    public VerkleMemoryDb GetReverseMergedDiff(long fromBlock, long toBlock)
    {
        return History.GetBatchDiff(fromBlock, toBlock).DiffLayer;
    }

    public void Reset()
    {
        BlockCache.Clear();
    }

    public event EventHandler<ReorgBoundaryReached>? ReorgBoundaryReached;

    private void InitRootHash()
    {
        InternalNode? node = GetInternalNode(Array.Empty<byte>());
        if (node is not null) return;
        Storage.SetInternalNode(Array.Empty<byte>(), new InternalNode(VerkleNodeType.BranchNode));
    }

    public byte[]? GetLeaf(ReadOnlySpan<byte> key)
    {
#if DEBUG
        if (key.Length != 32) throw new ArgumentException("key must be 32 bytes", nameof(key));
#endif
        using StackQueue<(long, VerkleMemoryDb)>.StackEnumerator diffs = BlockCache.GetStackEnumerator();
        while (diffs.MoveNext())
        {
            if (diffs.Current.Item2.LeafTable.TryGetValue(key, out byte[]? node)) return node;
        }

        return Storage.GetLeaf(key, out byte[]? value) ? value : null;
    }

    public InternalNode? GetInternalNode(ReadOnlySpan<byte> key)
    {
        using StackQueue<(long, VerkleMemoryDb)>.StackEnumerator diffs = BlockCache.GetStackEnumerator();
        while (diffs.MoveNext())
        {
            if (diffs.Current.Item2.InternalTable.TryGetValue(key, out InternalNode? node)) return node;
        }
        return Storage.GetInternalNode(key, out InternalNode? value) ? value : null;
    }

    public void SetLeaf(ReadOnlySpan<byte> leafKey, byte[] leafValue)
    {
#if DEBUG
        if (leafKey.Length != 32) throw new ArgumentException("key must be 32 bytes", nameof(leafKey));
        if (leafValue.Length != 32) throw new ArgumentException("value must be 32 bytes", nameof(leafValue));
#endif
        Storage.SetLeaf(leafKey, leafValue);
    }

    public void SetInternalNode(ReadOnlySpan<byte> internalNodeKey, InternalNode internalNodeValue)
    {
        Storage.SetInternalNode(internalNodeKey, internalNodeValue);
    }

    // This method is called at the end of each block to flush the batch changes to the storage and generate forward and reverse diffs.
    // this should be called only once per block, right now it does not support multiple calls for the same block number.
    // if called multiple times, the full state would be fine - but it would corrupt the diffs and historical state will be lost
    // TODO: add capability to update the diffs instead of overwriting if Flush(long blockNumber) is called multiple times for the same block number
    public void Flush(long blockNumber, VerkleMemoryDb batch)
    {
        if (blockNumber == 0)
        {
            PersistedStateRoot = GetStateRoot();
            FullStateCacheBlock = FullStatePersistedBlock = 0;
            PersistBlockChanges(batch, Storage);
            StateRoot = GetStateRoot();
            _stateRootToBlocks[StateRoot] = blockNumber;
            return;
        }
        if (blockNumber <= FullStateCacheBlock)
            throw new InvalidOperationException("Cannot flush for same block number multiple times");

        if (!BlockCache.EnqueueAndReplaceIfFull((blockNumber, batch), out (long, VerkleMemoryDb) element))
        {
            Pedersen root = GetStateRoot(element.Item2) ?? (new Pedersen(Storage.GetInternalNode(Array.Empty<byte>())?.InternalCommitment.Point.ToBytes().ToArray() ?? throw new ArgumentException()));
            PersistedStateRoot = root;
            VerkleMemoryDb reverseDiff = PersistBlockChanges(element.Item2, Storage);

            History.InsertDiff(element.Item1, element.Item2, reverseDiff);
            FullStatePersistedBlock = element.Item1;
            Storage.LeafDb.Flush();
            Storage.InternalNodeDb.Flush();
        }

        FullStateCacheBlock = blockNumber;
        StateRoot = GetStateRoot();
        _stateRootToBlocks[StateRoot] = blockNumber;
    }

    // now the full state back in time by one block.
    public void ReverseState()
    {

        if (BlockCache.Count != 0)
        {
            BlockCache.Pop(out _);
            return;
        }

        VerkleMemoryDb reverseDiff = History.GetBatchDiff(FullStatePersistedBlock, FullStatePersistedBlock - 1).DiffLayer;

        foreach (KeyValuePair<byte[], byte[]?> entry in reverseDiff.LeafTable)
        {
            reverseDiff.GetLeaf(entry.Key, out byte[]? node);
            if (node is null)
            {
                Storage.RemoveLeaf(entry.Key);
            }
            else
            {
                Storage.SetLeaf(entry.Key, node);
            }
        }

        foreach (KeyValuePair<byte[], InternalNode?> entry in reverseDiff.InternalTable)
        {
            reverseDiff.GetInternalNode(entry.Key, out InternalNode? node);
            if (node is null)
            {
                Storage.RemoveInternalNode(entry.Key);
            }
            else
            {
                Storage.SetInternalNode(entry.Key, node);
            }
        }
        FullStatePersistedBlock -= 1;
    }

    // use the batch diff to move the full state back in time to access historical state.
    public void ApplyDiffLayer(BatchChangeSet changeSet)
    {
        if (changeSet.FromBlockNumber != FullStatePersistedBlock)
            throw new ArgumentException($"Cannot apply diff FullStateBlock:{FullStatePersistedBlock}!=fromBlock:{changeSet.FromBlockNumber}", nameof(changeSet.FromBlockNumber));

        VerkleMemoryDb reverseDiff = changeSet.DiffLayer;

        foreach (KeyValuePair<byte[], byte[]?> entry in reverseDiff.LeafTable)
        {
            reverseDiff.GetLeaf(entry.Key, out byte[]? node);
            if (node is null)
            {
                Storage.RemoveLeaf(entry.Key);
            }
            else
            {
                Storage.SetLeaf(entry.Key, node);
            }
        }

        foreach (KeyValuePair<byte[], InternalNode?> entry in reverseDiff.InternalTable)
        {
            reverseDiff.GetInternalNode(entry.Key, out InternalNode? node);
            if (node is null)
            {
                Storage.RemoveInternalNode(entry.Key);
            }
            else
            {
                Storage.SetInternalNode(entry.Key, node);
            }
        }
        FullStatePersistedBlock = changeSet.ToBlockNumber;
    }

    public bool IsFullySynced(Keccak stateRoot) => _stateRootToBlocks[new Pedersen(stateRoot.Bytes)] != -2;

    public Pedersen GetStateRoot()
    {
        byte[] stateRoot = GetInternalNode(Array.Empty<byte>())?.InternalCommitment.Point.ToBytes().ToArray() ??
                           throw new InvalidOperationException();
        return new Pedersen(stateRoot);
    }

    private static Pedersen? GetStateRoot(IVerkleDb db)
    {
        return db.GetInternalNode(Array.Empty<byte>(), out InternalNode? node) ? new Pedersen(node!.InternalCommitment.Point.ToBytes().ToArray()) : null;
    }

    public bool MoveToStateRoot(Pedersen stateRoot)
    {
        Pedersen currentRoot = GetStateRoot();

        // if the target root node is same as current - return true
        if (currentRoot.Equals(stateRoot)) return true;
        // TODO: this is actually not possible - not sure if return true is correct here
        if (stateRoot.Equals(new Pedersen(Keccak.EmptyTreeHash.Bytes))) return true;

        long fromBlock = _stateRootToBlocks[currentRoot];
        if(fromBlock == -1) return false;
        long toBlock = _stateRootToBlocks[stateRoot];
        if (toBlock == -1) return false;

        if (fromBlock > toBlock)
        {
            long noOfBlockToMove = fromBlock - toBlock;
            if (noOfBlockToMove > BlockCache.Count)
            {
                BlockCache.Clear();
                fromBlock -= BlockCache.Count;

                BatchChangeSet batchDiff = History.GetBatchDiff(fromBlock, toBlock);
                ApplyDiffLayer(batchDiff);

            }
            else
            {
                for (int i = 0; i < noOfBlockToMove; i++)
                {
                    BlockCache.Pop(out _);
                }
            }
        }
        else
        {
            throw new NotImplementedException("Should be implemented in future (probably)");
        }

        Debug.Assert(GetStateRoot().Equals(stateRoot));
        return true;
    }

    private static VerkleMemoryDb PersistBlockChanges(VerkleMemoryDb blockChanges, VerkleKeyValueDb storage)
    {
        // we should not have any null values in the Batch db - because deletion of values from verkle tree is not allowed
        // nullable values are allowed in MemoryStateDb only for reverse diffs.
        VerkleMemoryDb reverseDiff = new();

        foreach (KeyValuePair<byte[], byte[]?> entry in blockChanges.LeafTable)
        {
            Debug.Assert(entry.Value is not null, "nullable value only for reverse diff");
            if (storage.GetLeaf(entry.Key, out byte[]? node)) reverseDiff.LeafTable[entry.Key] = node;
            else reverseDiff.LeafTable[entry.Key] = null;

            storage.SetLeaf(entry.Key, entry.Value);
        }

        foreach (KeyValuePair<byte[], InternalNode?> entry in blockChanges.InternalTable)
        {
            Debug.Assert(entry.Value is not null, "nullable value only for reverse diff");
            if (storage.GetInternalNode(entry.Key, out InternalNode? node)) reverseDiff.InternalTable[entry.Key] = node;
            else reverseDiff.InternalTable[entry.Key] = null;

            if(entry.Value.ShouldPersist) storage.SetInternalNode(entry.Key, entry.Value);
        }

        return reverseDiff;
    }

    private readonly struct StateRootToBlockMap
    {
        private readonly IDb _stateRootToBlock;

        public StateRootToBlockMap(IDb stateRootToBlock)
        {
            _stateRootToBlock = stateRootToBlock;
        }

        public long this[Pedersen key]
        {
            get
            {
                if (Pedersen.Zero.Equals(key)) return -1;
                byte[]? encodedBlock = _stateRootToBlock[key.Bytes];
                return encodedBlock is null ? -2 : BinaryPrimitives.ReadInt64LittleEndian(encodedBlock);
            }
            set
            {
                Span<byte> encodedBlock = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(encodedBlock, value);
                _stateRootToBlock.Set(key.Bytes, encodedBlock.ToArray());
            }
        }
    }
}
