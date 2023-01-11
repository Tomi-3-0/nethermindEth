// Copyright 2022 Demerzel Solutions Limited
// Licensed under Apache-2.0. For full terms, see LICENSE in the project root.

using Nethermind.Verkle.VerkleNodes;

namespace Nethermind.Verkle.VerkleStateDb;

public enum DiffType
{
    Forward,
    Reverse
}

public class DiffLayer : IDiffLayer
{
    private DiffType _diffType;
    private Dictionary<long, byte[]> Diff { get; }
    public DiffLayer(DiffType diffType)
    {
        Diff = new Dictionary<long, byte[]>();
        _diffType = diffType;
    }
    public void InsertDiff(long blockNumber, IVerkleDiffDb diff)
    {
        Diff[blockNumber] = diff.Encode();
    }
    public byte[] FetchDiff(long blockNumber) => Diff[blockNumber];
    public IVerkleDiffDb MergeDiffs(long fromBlock, long toBlock)
    {
        MemoryStateDb mergedDiff = new MemoryStateDb();
        switch (_diffType)
        {
            case DiffType.Reverse:
                for (long i = fromBlock; i <= toBlock; i++)
                {
                    byte[] currDiffBytes = FetchDiff(i);
                    MemoryStateDb reverseDiff = MemoryStateDb.Decode(currDiffBytes);
                    foreach (KeyValuePair<byte[], byte[]?> item in reverseDiff.LeafTable)
                    {
                        mergedDiff.LeafTable.TryAdd(item.Key, item.Value);
                    }
                    foreach (KeyValuePair<byte[], InternalNode?> item in reverseDiff.BranchTable)
                    {
                        mergedDiff.BranchTable.TryAdd(item.Key, item.Value);
                    }
                    foreach (KeyValuePair<byte[], SuffixTree?> item in reverseDiff.StemTable)
                    {
                        mergedDiff.StemTable.TryAdd(item.Key, item.Value);
                    }
                }
                break;
            case DiffType.Forward:
                for (long i = toBlock; i >= fromBlock; i--)
                {
                    byte[] currDiffBytes = FetchDiff(i);
                    MemoryStateDb reverseDiff = MemoryStateDb.Decode(currDiffBytes);
                    foreach (KeyValuePair<byte[], byte[]?> item in reverseDiff.LeafTable)
                    {
                        mergedDiff.LeafTable.TryAdd(item.Key, item.Value);
                    }
                    foreach (KeyValuePair<byte[], InternalNode?> item in reverseDiff.BranchTable)
                    {
                        mergedDiff.BranchTable.TryAdd(item.Key, item.Value);
                    }
                    foreach (KeyValuePair<byte[], SuffixTree?> item in reverseDiff.StemTable)
                    {
                        mergedDiff.StemTable.TryAdd(item.Key, item.Value);
                    }
                }
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        return mergedDiff;

    }
}