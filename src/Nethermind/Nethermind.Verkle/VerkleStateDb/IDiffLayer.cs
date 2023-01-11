// Copyright 2022 Demerzel Solutions Limited
// Licensed under Apache-2.0. For full terms, see LICENSE in the project root.

namespace Nethermind.Verkle.VerkleStateDb;

public interface IDiffLayer
{
    public void InsertDiff(long blockNumber, IVerkleDiffDb diff);

    public byte[] FetchDiff(long blockNumber);

    public IVerkleDiffDb MergeDiffs(long fromBlock, long toBlock);
}