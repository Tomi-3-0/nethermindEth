// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Diagnostics;
using Nethermind.Core.Extensions;
using Nethermind.Verkle.Curve;
using Nethermind.Verkle.Fields.FrEElement;
using Nethermind.Verkle.Tree.Nodes;
using Nethermind.Verkle.Tree.Proofs;
using Nethermind.Verkle.Tree.Utils;

namespace Nethermind.Verkle.Tree;

public partial class VerkleTree
{
    // public bool InsertIntoStatelessTree(VerkleProof proof, List<byte[]> keys, List<byte[]?> values, Banderwagon root)
    // {
    //     (bool, UpdateHint?) verification = Verify(proof, keys, values, root);
    //     if (!verification.Item1) return false;
    //
    //     InsertAfterVerification(verification.Item2!.Value, keys, values, root, false);
    //     return true;
    // }

    // public void InsertAfterVerification(UpdateHint hint, List<byte[]> keys, List<byte[]?> values, Banderwagon root, bool skipRoot = true)
    // {
    //     if (!skipRoot)
    //     {
    //         InternalNode rootNode = new(VerkleNodeType.BranchNode, new Commitment(root));
    //         _verkleStateStore.SetInternalNode(Array.Empty<byte>(), rootNode);
    //     }
    //
    //     AddStatelessInternalNodes(hint);
    //
    //     for (int i = 0; i < keys.Count; i++)
    //     {
    //         byte[]? value = values[i];
    //         if(value is null) continue;
    //         _verkleStateStore.SetLeaf(keys[i], value);
    //     }
    // }

    public void AddStatelessInternalNodes(UpdateHint hint, Dictionary<byte[], LeafUpdateDelta> subTrees)
    {
        List<byte> pathList = new();
        int stemIndex = 0;
        foreach ((byte[]? stem, (ExtPresent extStatus, byte depth)) in hint.DepthAndExtByStem)
        {
            pathList.Clear();
            for (int i = 0; i < depth - 1; i++)
            {
                pathList.Add(stem[i]);
                InternalNode node = new(VerkleNodeType.BranchNode, new Commitment(hint.CommByPath[pathList]));
                node.IsStateless = true;
                _verkleStateStore.SetInternalNode(pathList.ToArray(), node);
            }

            pathList.Add(stem[depth-1]);

            InternalNode stemNode;
            byte[] pathOfStem;
            switch (extStatus)
            {
                case ExtPresent.None:
                    stemNode =  new(VerkleNodeType.StemNode, stem, null, null, new Commitment());
                    pathOfStem = pathList.ToArray();
                    break;
                case ExtPresent.DifferentStem:
                    byte[] otherStem = hint.DifferentStemNoProof[pathList];
                    Commitment otherInternalCommitment = new(hint.CommByPath[pathList]);
                    stemNode = new(VerkleNodeType.StemNode, otherStem, null, null, otherInternalCommitment);
                    pathOfStem = pathList.ToArray();
                    break;
                case ExtPresent.Present:
                    Commitment internalCommitment = new(hint.CommByPath[pathList]);
                    Commitment? c1 = null;
                    Commitment? c2 = null;

                    pathList.Add(2);
                    if (hint.CommByPath.TryGetValue(pathList, out Banderwagon c1B)) c1 = new Commitment(c1B);
                    pathList[^1] = 3;
                    if (hint.CommByPath.TryGetValue(pathList, out Banderwagon c2B)) c2 = new Commitment(c2B);

                    stemNode = new(VerkleNodeType.StemNode, stem, c1, c2, internalCommitment);
                    pathOfStem = new byte[pathList.Count - 1];
                    pathList.CopyTo(0, pathOfStem, 0, pathList.Count - 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            _verkleStateStore.SetInternalNode(pathOfStem, stemNode);
        }
    }

    public static bool CreateStatelessTree(IVerkleStore store, VerkleProof proof, Banderwagon rootPoint, byte[] startStem, byte[] endStem, Dictionary<byte[], (byte, byte[])[]> subTrees)
    {
        const int numberOfStems = 2;
        List<Banderwagon> commSortedByPath = new(proof.CommsSorted.Length + 1) { rootPoint };
        commSortedByPath.AddRange(proof.CommsSorted);

        byte[][] stems = { startStem, endStem };

        // map stems to depth and extension status and create a list of stem with extension present
        Dictionary<byte[], (ExtPresent, byte)> depthsAndExtByStem = new(Bytes.EqualityComparer);
        HashSet<byte[]> stemsWithExtension = new(Bytes.EqualityComparer);
        for (int i = 0; i < numberOfStems; i++)
        {
            ExtPresent extPresent = proof.VerifyHint.ExtensionPresent[i];
            depthsAndExtByStem.Add(stems[i], (extPresent, proof.VerifyHint.Depths[i]));
            if (extPresent == ExtPresent.Present) stemsWithExtension.Add(stems[i]);
        }

        SortedSet<byte[]> otherStemsUsed = new(Bytes.Comparer);
        SortedSet<List<byte>> allPaths = new(new ListComparer());
        SortedSet<(List<byte>, byte)> allPathsAndZs = new(new ListWithByteComparer());
        Dictionary<(List<byte>, byte), FrE> leafValuesByPathAndZ = new(new ListWithByteEqualityComparer());
        SortedDictionary<List<byte>, byte[]> otherStemsByPrefix = new(new ListComparer());

        int prefixLength = 0;
        while (prefixLength<startStem.Length)
        {
            if (startStem[prefixLength] != endStem[prefixLength]) break;
            prefixLength++;
        }

        int keyIndex = 0;
        foreach (byte[] stem in stems)
        {
            (ExtPresent extPres, byte depth) = depthsAndExtByStem[stem];

            for (int i = 0; i < depth; i++)
            {
                allPaths.Add(new List<byte>(stem[..i]));
                if (i < prefixLength)
                {
                    allPathsAndZs.Add((new List<byte>(stem[..i]), stem[i]));
                    continue;
                }
                int startIndex = startStem[i];
                int endIndex = endStem[i];
                if (i > prefixLength)
                {
                    if (keyIndex == 0) endIndex = 255;
                    else startIndex = 0;
                }

                for (int j = startIndex; j <= endIndex; j++)
                {
                    allPathsAndZs.Add((new List<byte>(stem[..i]), (byte)j));
                }
            }

            switch (extPres)
            {
                case ExtPresent.DifferentStem:

                    allPaths.Add(new List<byte>(stem[..depth]));
                    allPathsAndZs.Add((new List<byte>(stem[..depth]), 0));
                    allPathsAndZs.Add((new List<byte>(stem[..depth]), 1));

                    byte[] otherStem;

                    // find the stems that are equal to the stem we are assuming to be without extension
                    // this happens when we initially added this stem when we were searching for another one
                    // but then in a future key, we found that we needed this stem too.
                    byte[][] found = stemsWithExtension.Where(x => x[..depth].SequenceEqual(stem[..depth])).ToArray();

                    switch (found.Length)
                    {
                        case 0:
                            found = proof.VerifyHint.DifferentStemNoProof.Where(x => x[..depth].SequenceEqual(stem[..depth])).ToArray();
                            byte[] encounteredStem = found[^1];
                            otherStem = encounteredStem;
                            otherStemsUsed.Add(encounteredStem);

                            // Add extension node to proof in particular, we only want to open at (1, stem)
                            leafValuesByPathAndZ[(new List<byte>(stem[..depth]), 0)] = FrE.One;
                            leafValuesByPathAndZ.Add((new List<byte>(stem[..depth]), 1), FrE.FromBytesReduced(encounteredStem.Reverse().ToArray()));
                            break;
                        case 1:
                            otherStem = found[0];
                            break;
                        default:
                            throw new InvalidDataException($"found more than one instance of stem_with_extension at depth {depth}, see: {string.Join(" | ", found.Select(x => string.Join(", ", x)))}");
                    }

                    otherStemsByPrefix.Add(stem[..depth].ToList(), otherStem);
                    break;
                case ExtPresent.Present:
                    allPaths.Add(new List<byte>(stem[..depth]));
                    allPathsAndZs.Add((new List<byte>(stem[..depth]), 0));
                    allPathsAndZs.Add((new List<byte>(stem[..depth]), 1));

                    leafValuesByPathAndZ[(new List<byte>(stem[..depth]), 0)] = FrE.One;
                    leafValuesByPathAndZ[(new List<byte>(stem[..depth]), 1)] = FrE.FromBytesReduced(stem.Reverse().ToArray());
                    break;
                case ExtPresent.None:
                    leafValuesByPathAndZ[depth == 1 ? (new List<byte>(), stem[depth - 1]) : (stem[..depth].ToList(), stem[depth - 1])] = FrE.Zero;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            keyIndex++;
        }

        Dictionary<List<byte>, Banderwagon> commByPath = new(new ListEqualityComparer());
        foreach ((List<byte> path, Banderwagon comm) in allPaths.Zip(commSortedByPath))
        {
            commByPath[path] = comm;
        }

        var stemsOfSubTree = subTrees.Keys.ToArray();

        List<byte[]> subTreesToCreate = UpdatePathsAndReturnSubTreesToCreate(allPaths, allPathsAndZs, stemsOfSubTree[1..^1]);
        Dictionary<byte[], LeafUpdateDelta> subTreeUpdates = GetSubTreeUpdates(subTrees);

        VerkleTree tree = new(store);

        List<byte> pathList = new();
        foreach ((byte[]? stem, (ExtPresent extStatus, byte depth)) in depthsAndExtByStem)
        {
            pathList.Clear();
            for (int i = 0; i < depth - 1; i++)
            {
                pathList.Add(stem[i]);
                InternalNode node = new(VerkleNodeType.BranchNode, new Commitment(commByPath[pathList]));
                node.IsStateless = true;
                tree._verkleStateStore.SetInternalNode(pathList.ToArray(), node);
            }

            pathList.Add(stem[depth-1]);

            InternalNode stemNode;
            byte[] pathOfStem;
            switch (extStatus)
            {
                case ExtPresent.None:
                    stemNode =  new(VerkleNodeType.StemNode, stem, null, null, new Commitment());
                    stemNode.IsStateless = true;
                    pathOfStem = pathList.ToArray();
                    break;
                case ExtPresent.DifferentStem:
                    byte[] otherStem = otherStemsByPrefix[pathList];
                    Commitment otherInternalCommitment = new(commByPath[pathList]);
                    stemNode = new(VerkleNodeType.StemNode, otherStem, null, null, otherInternalCommitment);
                    stemNode.IsStateless = true;
                    pathOfStem = pathList.ToArray();
                    break;
                case ExtPresent.Present:
                    Commitment internalCommitment = new(commByPath[pathList]);
                    // Commitment? c1 = null;
                    // Commitment? c2 = null;
                    //
                    // pathList.Add(2);
                    // if (hint.CommByPath.TryGetValue(pathList, out Banderwagon c1B)) c1 = new Commitment(c1B);
                    // pathList[^1] = 3;
                    // if (hint.CommByPath.TryGetValue(pathList, out Banderwagon c2B)) c2 = new Commitment(c2B);

                    stemNode = new(VerkleNodeType.StemNode, stem);
                    stemNode.IsStateless = true;
                    stemNode.UpdateCommitment(subTreeUpdates[stem]);
                    if (stemNode.InternalCommitment.Point != internalCommitment.Point) throw new ArgumentException();
                    pathOfStem = new byte[pathList.Count - 1];
                    pathList.CopyTo(0, pathOfStem, 0, pathList.Count - 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            tree._verkleStateStore.SetInternalNode(pathOfStem, stemNode);
        }

        byte[][] allStems = subTrees.Keys.ToArray()[1..^1];
        int stemIndex = 0;

        Dictionary<byte[], List<byte[]>> stemBatch = new(Bytes.EqualityComparer);
        foreach (byte[] stemPrefix in subTreesToCreate)
        {
            stemBatch.Add(stemPrefix, new List<byte[]>());
            while (stemIndex < allStems.Length)
            {
                if (Bytes.EqualityComparer.Equals(stemPrefix, allStems[stemIndex][..stemPrefix.Length]))
                {
                    stemBatch[stemPrefix].Add(allStems[stemIndex]);
                    stemIndex++;
                }
                else
                {
                    break;
                }
            }
        }

        Console.WriteLine("Queries");
        foreach (KeyValuePair<byte[], List<byte[]>> prefixWithStem in stemBatch)
        {
            foreach (byte[] stem in prefixWithStem.Value)
            {
                TraverseContext context = new(stem, subTreeUpdates[stem])
                    { CurrentIndex = prefixWithStem.Key.Length - 1 };
                tree.TraverseBranch(context);
            }

            commByPath[new List<byte>(prefixWithStem.Key)] = tree._verkleStateStore.GetInternalNode(prefixWithStem.Key)
                .InternalCommitment.Point;
        }

        // foreach (var xx in commByPath)
        // {
        //     Console.WriteLine($"{xx.Key.ToArray().ToHexString()} = {xx.Value.MapToScalarField().ToBytes().ToHexString()}");
        // }

        return VerifyVerkleProofStruct(proof.Proof, allPathsAndZs, leafValuesByPathAndZ, commByPath);
    }



    public bool CreateTreeFromRangeProofs(Banderwagon rootPoint, byte[] startStem, byte[] endStem, VerkleProof proof, (byte, byte[])[]? startSubtree, (byte, byte[])[]? endSubtree)
    {
        bool verified = VerifyVerkleRangeProof(proof,  startStem, endStem,new byte[][]{ startStem, endStem}, rootPoint, out UpdateHint? updateHint);
        if (!verified) throw new ArgumentException();
        UpdateHint hint = updateHint.Value;

        Debug.Assert(updateHint!.Value.DepthAndExtByStem.Count == 2);
        Dictionary<byte[], LeafUpdateDelta> subTrees = new(Bytes.EqualityComparer);

        Span<byte> key = new byte[32];
        switch (updateHint.Value.DepthAndExtByStem[startStem].Item1)
        {
            case ExtPresent.None:
                break;
            case ExtPresent.DifferentStem:
                break;
            case ExtPresent.Present:
                LeafUpdateDelta leafUpdateDeltaStem0 = new();

                startStem.CopyTo(key);
                foreach ((byte, byte[]) leafs in startSubtree)
                {
                    key[31] = leafs.Item1;
                    leafUpdateDeltaStem0.UpdateDelta(GetLeafDelta(leafs.Item2, leafs.Item1), key[31]);
                }

                subTrees[startStem] = leafUpdateDeltaStem0;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        LeafUpdateDelta leafUpdateDeltaStemLast = new();
        endStem.CopyTo(key);
        foreach ((byte, byte[]) leafs in endSubtree)
        {
            key[31] = leafs.Item1;
            leafUpdateDeltaStemLast.UpdateDelta(GetLeafDelta(leafs.Item2, leafs.Item1), key[31]);
        }

        subTrees[endStem] = leafUpdateDeltaStemLast;

        List<byte> pathList = new();
        foreach ((byte[]? stem, (ExtPresent extStatus, byte depth)) in hint.DepthAndExtByStem)
        {
            pathList.Clear();
            for (int i = 0; i < depth - 1; i++)
            {
                pathList.Add(stem[i]);
                InternalNode node = new(VerkleNodeType.BranchNode, new Commitment(hint.CommByPath[pathList]));
                node.IsStateless = true;
                _verkleStateStore.SetInternalNode(pathList.ToArray(), node);
            }

            pathList.Add(stem[depth-1]);

            InternalNode stemNode;
            byte[] pathOfStem;
            switch (extStatus)
            {
                case ExtPresent.None:
                    stemNode =  new(VerkleNodeType.StemNode, stem, null, null, new Commitment());
                    pathOfStem = pathList.ToArray();
                    break;
                case ExtPresent.DifferentStem:
                    byte[] otherStem = hint.DifferentStemNoProof[pathList];
                    Commitment otherInternalCommitment = new(hint.CommByPath[pathList]);
                    stemNode = new(VerkleNodeType.StemNode, otherStem, null, null, otherInternalCommitment);
                    pathOfStem = pathList.ToArray();
                    break;
                case ExtPresent.Present:
                    Commitment internalCommitment = new(hint.CommByPath[pathList]);
                    // Commitment? c1 = null;
                    // Commitment? c2 = null;
                    //
                    // pathList.Add(2);
                    // if (hint.CommByPath.TryGetValue(pathList, out Banderwagon c1B)) c1 = new Commitment(c1B);
                    // pathList[^1] = 3;
                    // if (hint.CommByPath.TryGetValue(pathList, out Banderwagon c2B)) c2 = new Commitment(c2B);

                    stemNode = new(VerkleNodeType.StemNode, stem);
                    stemNode.UpdateCommitment(subTrees[stem]);
                    if (stemNode.InternalCommitment.Point != internalCommitment.Point) throw new ArgumentException();
                    pathOfStem = new byte[pathList.Count - 1];
                    pathList.CopyTo(0, pathOfStem, 0, pathList.Count - 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            _verkleStateStore.SetInternalNode(pathOfStem, stemNode);
        }

        return true;
    }

    private static List<byte[]> UpdatePathsAndReturnSubTreesToCreate(IReadOnlySet<List<byte>> allPaths,
        ISet<(List<byte>, byte)> allPathsAndZs, IEnumerable<byte[]> stems)
    {
        List<byte[]> subTreesToCreate = new();
        foreach (byte[] stem in stems)
        {
            for (int i = 0; i < 31; i++)
            {
                List<byte> prefix = new(stem[..i]);
                if (allPaths.Contains(prefix))
                {
                    allPathsAndZs.Add((prefix, stem[i]));
                }
                else
                {
                    subTreesToCreate.Add(prefix.ToArray());
                    break;
                }
            }
        }

        return subTreesToCreate;
    }

    private static Dictionary<byte[], LeafUpdateDelta> GetSubTreeUpdates(Dictionary<byte[], (byte, byte[])[]> subTrees)
    {
        Dictionary<byte[], LeafUpdateDelta> subTreeUpdates = new(Bytes.EqualityComparer);
        foreach (KeyValuePair<byte[], (byte, byte[])[]> subTree in subTrees)
        {
            LeafUpdateDelta leafUpdateDelta = new();
            foreach ((byte, byte[]) leafs in subTree.Value)
            {
                leafUpdateDelta.UpdateDelta(GetLeafDelta(leafs.Item2, leafs.Item1), leafs.Item1);
            }

            subTreeUpdates[subTree.Key] = leafUpdateDelta;
        }

        return subTreeUpdates;
    }
}