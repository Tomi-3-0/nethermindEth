// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nethermind.Core.Extensions;
using Nethermind.JsonRpc;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.Handlers;

namespace Nethermind.Merge.Plugin;

public partial class EngineRpcModule : IEngineRpcModule
{
    private readonly IAsyncHandler<byte[], GetPayloadV3Result?> _getPayloadHandlerV3;

    public async Task<ResultWrapper<PayloadStatusV1>> engine_newPayloadV3(ExecutionPayload executionPayload, byte[]?[]? blobVersionedHashes = null) =>
        (await Validate(executionPayload, blobVersionedHashes)) ?? await NewPayload(executionPayload, 3);

    private async Task<ResultWrapper<PayloadStatusV1>?> Validate(ExecutionPayload executionPayload, byte[]?[]? blobVersionedHashes)
    {
        ResultWrapper<PayloadStatusV1> ErrorResult(string error)
        {
            if (_logger.IsWarn) _logger.Warn(error);
            return ResultWrapper<PayloadStatusV1>.Success(
                new PayloadStatusV1
                {
                    Status = PayloadStatus.Invalid,
                    LatestValidHash = null,
                    ValidationError = error
                });
        }

        static IEnumerable<byte[]?> FlattenHashesFromTransactions(ExecutionPayload payload) =>
            payload.GetTransactions()
                .Where(t => t.BlobVersionedHashes is not null)
                .SelectMany(t => t.BlobVersionedHashes!);

        if (!_specProvider.GetSpec(executionPayload!.BlockNumber, executionPayload.Timestamp).IsEip4844Enabled)
        {
            if (executionPayload?.DataGasUsed is not null || executionPayload?.ExcessDataGas is not null || blobVersionedHashes is not null)
            {
                ResultWrapper<PayloadStatusV1>.Fail("Cancun params are not empty", ErrorCodes.InvalidParams);
            }

            return await engine_newPayloadV2(executionPayload!);
        }
        else
        {
            return blobVersionedHashes is null || executionPayload?.DataGasUsed is null || executionPayload?.ExcessDataGas is null
                ? ResultWrapper<PayloadStatusV1>.Fail("Invalid Cancun params", ErrorCodes.InvalidParams)
                : !FlattenHashesFromTransactions(executionPayload).SequenceEqual(blobVersionedHashes, Bytes.NullableEqualityComparer) ? ErrorResult("Blob versioned hashes do not match")
                : null;
        }

    }

    public async Task<ResultWrapper<GetPayloadV3Result?>> engine_getPayloadV3(byte[] payloadId) =>
        await _getPayloadHandlerV3.HandleAsync(payloadId);
}
