// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Linq;
using Nethermind.Blockchain.Receipts;
using Nethermind.Consensus.Rewards;
using Nethermind.Consensus.Validators;
using Nethermind.Consensus.Withdrawals;
using Nethermind.Core;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Crypto;
using Nethermind.Evm.Tracing;
using Nethermind.Logging;
using Nethermind.State;
using Nethermind.Verkle.Curve;

namespace Nethermind.Consensus.Processing;

public class StatelessBlockProcessor: BlockProcessor
{
    private readonly ILogger _logger;

    public StatelessBlockProcessor(
        ISpecProvider? specProvider,
        IBlockValidator? blockValidator,
        IRewardCalculator? rewardCalculator,
        IBlockProcessor.IBlockTransactionsExecutor? blockTransactionsExecutor,
        IWorldState? stateProvider,
        IReceiptStorage? receiptStorage,
        IWitnessCollector? witnessCollector,
        ILogManager? logManager,
        IWithdrawalProcessor? withdrawalProcessor = null)
        : base(
            specProvider,
            blockValidator,
            rewardCalculator,
            blockTransactionsExecutor,
            stateProvider,
            receiptStorage,
            witnessCollector,
            logManager,
            withdrawalProcessor)
    {
        _logger = logManager?.GetClassLogger<StatelessBlockProcessor>() ?? throw new ArgumentNullException(nameof(logManager));
    }

    protected override TxReceipt[] ProcessBlock(
        Block block,
        IBlockTracer blockTracer,
        ProcessingOptions options)
    {

        IBlockProcessor.IBlockTransactionsExecutor? blockTransactionsExecutor;
        IWorldState worldState;
        if (!block.IsGenesis)
        {
            block.Header.MaybeParent.TryGetTarget(out BlockHeader maybeParent);
            worldState = new VerkleWorldState(block.Header.Witness!.Value, Banderwagon.FromBytes(maybeParent.StateRoot.Bytes)!.Value, SimpleConsoleLogManager.Instance);
            blockTransactionsExecutor = _blockTransactionsExecutor.WithNewStateProvider(worldState);
        }
        else
        {
            blockTransactionsExecutor = _blockTransactionsExecutor;
            worldState = _stateProvider;
        }

        _logger.Info($"ProcessBlock Before: {worldState.StateRoot.Bytes.ToHexString()}");
        IReleaseSpec spec = _specProvider.GetSpec(block.Header);

        _receiptsTracer.SetOtherTracer(blockTracer);
        _receiptsTracer.StartNewBlockTrace(block);
        TxReceipt[] receipts = blockTransactionsExecutor.ProcessTransactions(block, options, _receiptsTracer, spec);
        _logger.Info($"ProcessBlock AfterPT: {worldState.StateRoot.Bytes.ToHexString()}");

        block.Header.ReceiptsRoot = receipts.GetReceiptsRoot(spec, block.ReceiptsRoot);
        ApplyMinerRewards(block, blockTracer, spec);
        _withdrawalProcessor.ProcessWithdrawals(block, spec);
        _receiptsTracer.EndBlockTrace();

        // if we are producing blocks - then calculate and add the witness to the block
        // but we need to remember that the witness needs to be generate for the parent block state (for pre state)
        _logger.Info($"TEST BLOCK PROCESSOR VIRTUAL: {spec.IsVerkleTreeEipEnabled} {!block.IsGenesis} {options}");
        _logger.Info($"we are adding witness");
        byte[][]? usedKeysCac = _receiptsTracer.WitnessKeys.ToArray();
        _logger.Info($"UsedKeys {usedKeysCac.Length}");
        foreach (var key in usedKeysCac)
        {
            _logger.Info($"{key.ToHexString()}");
        }
        if (options.ContainsFlag(ProcessingOptions.ProducingBlock) && spec.IsVerkleTreeEipEnabled && !block.IsGenesis)
        {
            throw new ArgumentException("cannot produce block only process");
        }

        worldState.Commit(spec);
        worldState.RecalculateStateRoot();

        block.Header.StateRoot = worldState.StateRoot;
        block.Header.Hash = block.Header.CalculateHash();

        return receipts;
    }
}