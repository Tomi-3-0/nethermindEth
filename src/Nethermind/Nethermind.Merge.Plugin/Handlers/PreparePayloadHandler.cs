//  Copyright (c) 2021 Demerzel Solutions Limited
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
// 

using System;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Producers;
using Nethermind.Consensus;
using Nethermind.Core;
using Nethermind.JsonRpc;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;

namespace Nethermind.Merge.Plugin.Handlers
{
    public class PreparePayloadHandler: IHandlerAsync<PreparePayloadRequest, PreparePayloadResult>
    {
        private readonly IBlockTree _blockTree;
        private readonly PayloadStorage _payloadStorage;
        private readonly IManualBlockProductionTrigger _blockProductionTrigger;
        private readonly IManualBlockProductionTrigger _emptyBlockProductionTrigger;
        private readonly ManualTimestamper _timestamper;
        private readonly ISealer _sealer;
        private readonly ILogger _logger;
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

        public PreparePayloadHandler(
            IBlockTree blockTree, 
            PayloadStorage payloadStorage, 
            IManualBlockProductionTrigger blockProductionTrigger, 
            IManualBlockProductionTrigger emptyBlockProductionTrigger, 
            ManualTimestamper timestamper, 
            ISealer sealer,
            ILogManager logManager)
        {
            _blockTree = blockTree;
            _payloadStorage = payloadStorage;
            _blockProductionTrigger = blockProductionTrigger;
            _emptyBlockProductionTrigger = emptyBlockProductionTrigger;
            _timestamper = timestamper;
            _sealer = sealer;
            _logger = logManager.GetClassLogger();
        }

        public async Task<ResultWrapper<PreparePayloadResult>> HandleAsync(PreparePayloadRequest request)
        {
            // add syncing check when implementation will be ready
            // if (_ethSyncingInfo.IsSyncing())
            // {
            //     executePayloadResult.Status = VerificationStatus.Syncing;
            //     return ResultWrapper<PreparePayloadResult>.Fail($"Syncing in progress.",MergeErrorCodes.ActionNotAllowed);
            // }
            
            BlockHeader? parentHeader = _blockTree.FindHeader(request.ParentHash);
            if (parentHeader is null)
            {
                if (_logger.IsWarn) _logger.Warn($"Parent block {request.ParentHash} cannot be found. New block will not be produced.");
                return ResultWrapper<PreparePayloadResult>.Fail(
                    $"Parent block {request.ParentHash} cannot be found. New block will not be produced.",
                    MergeErrorCodes.UnknownHeader);
            }

            _timestamper.Set(DateTimeOffset.FromUnixTimeSeconds((long) request.Timestamp).UtcDateTime);
            using CancellationTokenSource cts = new(_timeout);

            uint payloadId = _payloadStorage.RentNextPayloadId();
            Address blockAuthor = request.FeeRecipient == Address.Zero ? _sealer.Address : request.FeeRecipient;
            Block? emptyBlock = await _emptyBlockProductionTrigger.BuildBlock(parentHeader, cts.Token, null, blockAuthor, request.Timestamp);
            Task<Block?> idealBlock = _blockProductionTrigger.BuildBlock(parentHeader, cts.Token, null, blockAuthor, request.Timestamp);
            _payloadStorage.AddPayload(payloadId, request.Random, emptyBlock, idealBlock); // not awaiting on purpose
            
            return ResultWrapper<PreparePayloadResult>.Success(new PreparePayloadResult(payloadId));
        }
    }
}
