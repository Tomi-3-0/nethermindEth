//  Copyright (c) 2018 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Nethermind.Blockchain.Processing;
using Nethermind.Core.Crypto;
using Nethermind.DataMarketplace.Consumers.Deposits.Domain;
using Nethermind.DataMarketplace.Consumers.Deposits.Queries;
using Nethermind.DataMarketplace.Consumers.Deposits.Repositories;
using Nethermind.DataMarketplace.Consumers.Notifiers;
using Nethermind.DataMarketplace.Consumers.Shared.Background;
using Nethermind.DataMarketplace.Core.Domain;
using Nethermind.DataMarketplace.Core.Services;
using Nethermind.Facade.Proxy;
using Nethermind.Facade.Proxy.Models;
using Nethermind.Logging;
using Timer = System.Timers.Timer;

namespace Nethermind.DataMarketplace.Consumers.Shared.Services
{
    public class ConsumerServicesBackgroundProcessor : IConsumerServicesBackgroundProcessor, IDisposable
    {
        private readonly IDepositDetailsRepository _depositRepository;
        private readonly IConsumerNotifier _consumerNotifier;
        private readonly bool _useDepositTimer;
        private readonly IEthJsonRpcClientProxy? _ethJsonRpcClientProxy;
        private readonly IEthPriceService _ethPriceService;
        private readonly IBackgroundDepositService _backgroundDepositService;
        private readonly IBackgroundRefundService _backgroundRefundService;
        private readonly IGasPriceService _gasPriceService;
        private readonly IBlockProcessor _blockProcessor;
        private readonly ILogger _logger;

        private Timer? _depositTimer;
        private uint _depositTimerPeriod;
        private long _currentBlockTimestamp;
        private long _currentBlockNumber;

        public ConsumerServicesBackgroundProcessor(
            IEthPriceService ethPriceService,
            IBackgroundDepositService backgroundDepositService,
            IBackgroundRefundService backgroundRefundService,
            IGasPriceService gasPriceService,
            IBlockProcessor blockProcessor,
            IDepositDetailsRepository depositRepository,
            IConsumerNotifier consumerNotifier,
            ILogManager logManager,
            bool useDepositTimer = false,
            IEthJsonRpcClientProxy? ethJsonRpcClientProxy = null,
            uint depositTimer = 10000)
        {
            _ethPriceService = ethPriceService;
            _backgroundDepositService = backgroundDepositService;
            _backgroundRefundService = backgroundRefundService;
            _gasPriceService = gasPriceService;
            _blockProcessor = blockProcessor;
            _depositRepository = depositRepository;
            _consumerNotifier = consumerNotifier;
            _useDepositTimer = useDepositTimer;
            _ethJsonRpcClientProxy = ethJsonRpcClientProxy;
            _logger = logManager.GetClassLogger();
            _ethPriceService.UpdateAsync();
            _gasPriceService.UpdateAsync();
            _depositTimerPeriod = depositTimer;
        }

        public void Init()
        {
            if (_useDepositTimer)
            {
                if (_depositTimer == null)
                {
                    if (_ethJsonRpcClientProxy == null)
                    {
                        if (_logger.IsError) _logger.Error("Cannot find any configured ETH proxy to run deposit timer.");
                        return;
                    }

                    _depositTimer = new Timer(_depositTimerPeriod);
                    _depositTimer.Elapsed += DepositTimerOnElapsed;
                    _depositTimer.Start();
                }

                if (_logger.IsInfo) _logger.Info("Initialized NDM consumer services background processor.");
            }
            else
            {
                _blockProcessor.BlockProcessed += OnBlockProcessed;
            }
        }

        private void DepositTimerOnElapsed(object sender, ElapsedEventArgs e)
            => _ethJsonRpcClientProxy?.eth_getBlockByNumber(BlockParameterModel.Latest)
                .ContinueWith(async t =>
                {
                    if (t.IsFaulted && _logger.IsError)
                    {
                        _logger.Error("Fetching the latest block via proxy has failed.", t.Exception);
                        return;
                    }

                    BlockModel<Keccak>? block = t.Result?.IsValid == true ? t.Result.Result : null;
                    if (block is null)
                    {
                        _logger.Error("Latest block fetched via proxy is null.", t.Exception);
                        return;
                    }

                    if (_currentBlockNumber == block.Number)
                    {
                        return;
                    }

                    await ProcessBlockAsync((long) block.Number, (long) block.Timestamp);
                });


        private void OnBlockProcessed(object? sender, BlockProcessedEventArgs e)
            => ProcessBlockAsync(e.Block.Number, (long) e.Block.Timestamp).ContinueWith(t =>
            {
                if (t.IsFaulted && _logger.IsError)
                {
                    _logger.Error($"Processing the block {e.Block.Number} has failed.", t.Exception);
                }
            });

        private async Task ProcessBlockAsync(long number, long timestamp)
        {
            Interlocked.Exchange(ref _currentBlockNumber, number);
            Interlocked.Exchange(ref _currentBlockTimestamp, timestamp);
            await _consumerNotifier.SendBlockProcessedAsync(number);
            PagedResult<DepositDetails> depositsToConfirm = await _depositRepository.BrowseAsync(new GetDeposits
            {
                OnlyUnconfirmed = true,
                OnlyNotRejected = true,
                Results = int.MaxValue
            });

            await _backgroundDepositService.TryConfirmDepositsAsync(depositsToConfirm.Items);
            PagedResult<DepositDetails> depositsToRefund = await _depositRepository.BrowseAsync(new GetDeposits
            {
                EligibleToRefund = true,
                CurrentBlockTimestamp = _currentBlockTimestamp,
                Results = int.MaxValue
            });

            await _backgroundRefundService.TryClaimRefundsAsync(depositsToRefund.Items);
            await _ethPriceService.UpdateAsync();
            await _consumerNotifier.SendEthUsdPriceAsync(_ethPriceService.UsdPrice, _ethPriceService.UpdatedAt);
            await _gasPriceService.UpdateAsync();

            if (_gasPriceService.Types != null)
            {
                await _consumerNotifier.SendGasPriceAsync(_gasPriceService.Types);
            }
        }

        public void Dispose()
        {
            _depositTimer?.Dispose();
        }
    }
}
