using System;
using System.Collections.Generic;
using System.Linq;
using Jint.Native;
using Nethermind.Blockchain.Find;
using Nethermind.Core;
using Nethermind.Int256;
using Nethermind.TxPool;

namespace Nethermind.JsonRpc.Modules.Eth
{
    public class GasPriceOracle : IGasPriceOracle
    {
        private Block? _lastHeadBlock;
        private UInt256? _lastGasPrice;
        private UInt256? _defaultGasPrice;
        private readonly UInt256? _ignoreUnder;
        private readonly IBlockFinder _blockFinder;
        private readonly int _blockLimit;
        private readonly int _txThreshold;
        private bool _eip1559Enabled;
        private readonly UInt256 _baseFee;
        public  const int NoHeadBlockChangeErrorCode = 7;
        private const int Percentile = 40;
        private const int DefaultBlocksToGoBack = 20;
        private const int DefaultBaseFee = 200;
        private const int BlockLimitForDefaultGasPrice = 8;

        public GasPriceOracle(IBlockFinder blockFinder, bool eip1559Enabled = false, UInt256? ignoreUnder = null, 
            int? blockLimit = null, UInt256? baseFee = null)
        {
            _blockFinder = blockFinder ?? throw new ArgumentNullException(nameof(blockFinder));
            _eip1559Enabled = eip1559Enabled;
            _ignoreUnder = ignoreUnder ?? UInt256.Zero;
            _blockLimit = blockLimit ?? DefaultBlocksToGoBack;
            _txThreshold = SetTxThreshold(_blockLimit);
            _baseFee = baseFee ?? DefaultBaseFee;
        }

        private int SetTxThreshold(int blocksToGoBack)
        {
            return blocksToGoBack * 2;
        }

        private bool AddedMoreThanOneTx(Block block, ref SortedSet<UInt256> sortedSet)
        {
            Transaction[] transactionsInBlock = block.Transactions;
            int countTxAdded;

            if (TransactionsExistIn(transactionsInBlock))
            {
                countTxAdded = AddTxAndReturnNumberAdded(sortedSet, transactionsInBlock);

                if (countTxAdded == 0)
                {
                    AddDefaultPriceTo(sortedSet);
                }
                
                return MoreThanOneTransactionAdded(countTxAdded);
            }
            else
            {
                AddDefaultPriceTo(sortedSet);
                return false;
            }
        }

        private int AddTxAndReturnNumberAdded(SortedSet<UInt256> sortedSet, Transaction[] transactionsInBlock)
        {
            int countTxAdded = 0;
            
            foreach (Transaction transaction in transactionsInBlock)
            {
                if (TransactionCanBeAdded(transaction, _eip1559Enabled)) //how should i set to be null?
                {
                    sortedSet.Add(CalculateEffectiveGasPriceOf(transaction));
                    countTxAdded++;
                }
            }

            return countTxAdded;
        }

        private UInt256 CalculateEffectiveGasPriceOf(Transaction transaction)
        {
            return transaction.CalculateEffectiveGasPrice(_eip1559Enabled, _baseFee);
        }

        private void AddDefaultPriceTo(SortedSet<UInt256> sortedSet)
        {
            sortedSet.Add((UInt256) _defaultGasPrice!);
        }

        private bool TransactionCanBeAdded(Transaction transaction, bool eip1559Enabled)
        {
            return IsAboveMinPrice(transaction) && Eip1559ModeCompatible(transaction, eip1559Enabled);
        }

        private bool IsAboveMinPrice(Transaction transaction)
        {
            return transaction.GasPrice >= _ignoreUnder;
        }

        private bool Eip1559ModeCompatible(Transaction transaction, bool eip1559Enabled)
        {
            if (eip1559Enabled == false)
            {
                return TransactionIsNotEip1559(transaction);
            }
            else
            {
                return true;
            }
        }

        private static bool TransactionIsNotEip1559(Transaction transaction)
        {
            return !transaction.IsEip1559;
        }

        private static bool MoreThanOneTransactionAdded(int added)
        {
            return added > 1;
        }

        private void SetDefaultGasPrice(long headBlockNumber)
        {
            Transaction[] transactions;
            int blocksToCheck = BlockLimitForDefaultGasPrice;
            
            while (headBlockNumber >= 0 && DefaultGasPriceBlockLimitNotReached(ref blocksToCheck))
            {
                transactions = GetTxFromBlockWithNumber(headBlockNumber);
                if (_eip1559Enabled)
                {
                    transactions = transactions.Where(tx => tx.IsEip1559).ToArray();
                }

                if (TransactionsExistIn(transactions))
                {
                    _defaultGasPrice = transactions[^1].GasPrice;
                    return;
                }
                
                headBlockNumber--;
            }
            _defaultGasPrice = 1; 
        }

        private static bool DefaultGasPriceBlockLimitNotReached(ref int blocksToCheck)
        {
            return blocksToCheck-- > 0;
        }

        private Transaction[] GetTxFromBlockWithNumber(long headBlockNumber)
        {
            return _blockFinder.FindBlock(headBlockNumber)!.Transactions;
        }

        private static bool TransactionsExistIn(Transaction[] transactions)
        {
            return transactions.Length > 0;
        }

        private SortedSet<UInt256> AddingTxPricesFromNewestToOldestBlock(SortedSet<UInt256> gasPrices)
        {
            long currentBlockNumber = GetHeadBlock()!.Number;
            int blocksToGoBack = _blockLimit;
            while (MoreBlocksToGoBack(blocksToGoBack) && CurrentBlockNumberIsValid(currentBlockNumber)) 
            {
                Block? block = _blockFinder.FindBlock(currentBlockNumber);
                if (BlockExists(block))
                {
                    bool moreThanOneTxAdded = AddedMoreThanOneTx(block, ref gasPrices);
                    if (moreThanOneTxAdded || BonusBlockLimitReached(gasPrices, blocksToGoBack))
                    {
                        blocksToGoBack--;
                    }
                }
                currentBlockNumber--;
            }

            return gasPrices;
        }

        private Block? GetHeadBlock()
        {
            return _blockFinder.FindHeadBlock();
        }

        private bool BonusBlockLimitReached(SortedSet<UInt256> gasPrices, int blocksToGoBack)
        {
            return gasPrices.Count + blocksToGoBack >= _txThreshold;
        }

        private static bool BlockExists(Block? foundBlock)
        {
            return foundBlock != null;
        }

        private static bool CurrentBlockNumberIsValid(long currBlockNumber)
        {
            return currBlockNumber > -1;
        }

        private static bool MoreBlocksToGoBack(long blocksToGoBack)
        {
            return blocksToGoBack > 0;
        }

        private ResultWrapper<UInt256?> HandleMissingHeadOrGenesisBlockCase(Block? headBlock, Block? genesisBlock)
        {
            if (BlockDoesNotExist(headBlock))
            {
                return ResultWrapper<UInt256?>.Fail("The head block had a null value.");
            }
            else if (BlockDoesNotExist(genesisBlock))
            {
                return ResultWrapper<UInt256?>.Fail("The genesis block had a null value.");
            }
            else
            {
                return ResultWrapper<UInt256?>.Success(UInt256.Zero);
            }
        }

        private static bool BlockDoesNotExist(Block? block)
        {
            return block == null;
        }

        private ResultWrapper<UInt256?> HandleNoHeadBlockChange(Block? headBlock)
        {
            ResultWrapper<UInt256?> resultWrapper;
            
            if (LastGasPriceExists() && LastHeadBlockExists() && LastHeadIsSameAsCurrentHead(headBlock))
            {
                resultWrapper = ResultWrapper<UInt256?>.Success(_lastGasPrice);
#if DEBUG
                resultWrapper.ErrorCode = NoHeadBlockChangeErrorCode;
#endif
                return resultWrapper;
            }
            else
            {
                return ResultWrapper<UInt256?>.Fail("");
            }
        }

        private bool LastGasPriceExists()
        {
            return _lastGasPrice != null;
        }

        private bool LastHeadBlockExists()
        {
            return _lastHeadBlock != null;
        }
        
        private bool LastHeadIsSameAsCurrentHead(Block? headBlock)
        {
            return headBlock!.Hash == _lastHeadBlock!.Hash;
        }


        public ResultWrapper<UInt256?> GasPriceEstimate()
        {
            Tuple<bool, ResultWrapper<UInt256?>> earlyExitResult = EarlyExitAndResult();
            if (earlyExitResult.Item1 == true)
            {
                return earlyExitResult.Item2;
            }
            
            SetDefaultGasPrice(_lastHeadBlock!.Number);
            
            SortedSet<UInt256> gasPricesSetHandlingDuplicates = CreateAndAddTxGasPricesToSet();
            
            UInt256? gasPriceEstimate = GasPriceAtPercentile(gasPricesSetHandlingDuplicates);

            SetLastGasPrice(gasPriceEstimate);
            
            return ResultWrapper<UInt256?>.Success((UInt256) gasPriceEstimate!);
        }

        private void SetLastGasPrice(UInt256? lastGasPrice)
        {
            _lastGasPrice = lastGasPrice;
        }

        private static UInt256? GasPriceAtPercentile(SortedSet<UInt256> gasPricesSetHandlingDuplicates)
        {
            int roundedIndex = GetRoundedIndexAtPercentile(gasPricesSetHandlingDuplicates);

            UInt256? gasPriceEstimate = GetElementAtIndex(gasPricesSetHandlingDuplicates, roundedIndex);

            return gasPriceEstimate;
        }

        private static int GetRoundedIndexAtPercentile(SortedSet<UInt256> gasPricesSetHandlingDuplicates)
        {
            int lastIndex = gasPricesSetHandlingDuplicates.Count - 1;
            float percentileOfLastIndex = lastIndex * ((float) Percentile / 100);
            int roundedIndex = (int) Math.Round(percentileOfLastIndex);
            return roundedIndex;
        }

        private static UInt256 GetElementAtIndex(SortedSet<UInt256> gasPricesSetHandlingDuplicates, int roundedIndex)
        {
            UInt256[] arrayOfGasPrices = gasPricesSetHandlingDuplicates.ToArray();
            return arrayOfGasPrices[roundedIndex];
        }

        private SortedSet<UInt256> CreateAndAddTxGasPricesToSet()
        {
            SortedSet<UInt256> gasPricesSetHandlingDuplicates = new(GetDuplicateComparer());
            gasPricesSetHandlingDuplicates = AddingTxPricesFromNewestToOldestBlock(gasPricesSetHandlingDuplicates);
            return gasPricesSetHandlingDuplicates;
        }

        private Tuple<bool, ResultWrapper<UInt256?>> EarlyExitAndResult()
        {
            Block? headBlock = GetHeadBlock();
            Block? genesisBlock = GetGenesisBlock();
            ResultWrapper<UInt256?> resultWrapper;

            resultWrapper = HandleMissingHeadOrGenesisBlockCase(headBlock, genesisBlock);
            if (ResultWrapperWasNotSuccessful(resultWrapper))
            {
                return BoolAndWrapperTuple(true, resultWrapper);
            }

            resultWrapper = HandleNoHeadBlockChange(headBlock);
            if (ResultWrapperWasSuccessful(resultWrapper))
            {
                return BoolAndWrapperTuple(true, resultWrapper);
            }
            SetLastHeadBlock(headBlock);
            return BoolAndWrapperTuple(false, resultWrapper);
        }

        private void SetLastHeadBlock(Block? headBlock)
        {
            _lastHeadBlock = headBlock;
        }

        private static Tuple<bool, ResultWrapper<UInt256?>> BoolAndWrapperTuple(bool boolean, ResultWrapper<UInt256?> resultWrapper)
        {
            return new(boolean, resultWrapper);
        }

        private Block? GetGenesisBlock()
        {
            return _blockFinder.FindGenesisBlock();
        }

        private static bool ResultWrapperWasSuccessful(ResultWrapper<UInt256?> resultWrapper)
        {
            return resultWrapper.Result == Result.Success;
        }
        
        private static bool ResultWrapperWasNotSuccessful(ResultWrapper<UInt256?> resultWrapper)
        {
            return resultWrapper.Result != Result.Success;
        }
        
        private static Comparer<UInt256> GetDuplicateComparer()
        {
            Comparer<UInt256> comparerForDuplicates = Comparer<UInt256>.Create(((a, b) =>
                        {
                            int res = a.CompareTo(b);
                            return res == 0 ? 1 : res;
                        }
                    )
                );
            return comparerForDuplicates;
        }
    }
}
