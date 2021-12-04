﻿//  Copyright (c) 2021 Demerzel Solutions Limited
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nethermind.JsonRpc.Modules;
using Nethermind.JsonRpc.Test.Modules;
using Nethermind.Logging;
using Nethermind.Serialization.Json;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Nethermind.JsonRpc.Test
{
    public static class RpcTest
    {
        public static JsonRpcResponse TestRequest<T>(T module, string method, params string[] parameters)
            where T : class, IRpcModule
        {
            IJsonRpcService service = BuildRpcService(module);
            JsonRpcRequest request = GetJsonRequest(method, parameters: parameters);
            return service.SendRequestAsync(request, new JsonRpcContext(RpcEndpoint.Http)).Result;
        }

        public static string TestSerializedRequest<T>(IReadOnlyCollection<JsonConverter> converters, T module,
            string method, params string[] parameters) where T : class, IRpcModule
        {
            JsonRpcRequest request = GetJsonRequest(method, parameters: parameters);
            return SendAndSerializeRequest(converters, module, request);
        }

        public static string TestSerializedRequest<T>(T module, string method, params string[] parameters)
            where T : class, IRpcModule
        {
            return TestSerializedRequest(new JsonConverter[0], module, method, parameters);
        }

        public static string TestSerializedRequest<T>(T module, string method, int id, params string[] parameters)
            where T : class, IRpcModule
        {
            JsonRpcRequest request = GetJsonRequest(method, id, parameters);
            return  SendAndSerializeRequest(new JsonConverter[0], module, request);
        }

        public static IJsonRpcService BuildRpcService<T>(T module) where T : class, IRpcModule
        {
            var moduleProvider = new TestRpcModuleProvider<T>(module);
            moduleProvider.Register(new SingletonModulePool<T>(new SingletonFactory<T>(module), true));
            IJsonRpcService service = new JsonRpcService(moduleProvider, LimboLogs.Instance);
            return service;
        }

        public static JsonRpcRequest GetJsonRequest(string method, int? id = null, params string[] parameters)
        {
            var request = new JsonRpcRequest()
            {
                JsonRpc = "2.0", Method = method, Params = parameters?.ToArray() ?? new string[0], Id = id ?? 67
            };

            return request;
        }

        private static string SendAndSerializeRequest<T>(IReadOnlyCollection<JsonConverter> converters, T module,
            JsonRpcRequest request) where T : class, IRpcModule
        {
            IJsonRpcService service = BuildRpcService(module);

            JsonRpcContext context = new JsonRpcContext(RpcEndpoint.Http);
            if (module is IContextAwareRpcModule contextAwareModule
                && contextAwareModule.Context != null)
            {
                context = contextAwareModule.Context;
            }

            JsonRpcResponse response = service.SendRequestAsync(request, context).Result;

            EthereumJsonSerializer serializer = new();
            foreach (JsonConverter converter in converters)
            {
                serializer.RegisterConverter(converter);
            }

            Stream stream = new MemoryStream();
            long size = serializer.Serialize(stream, response);

            // for coverage (and to prove that it does not throw
            Stream indentedStream = new MemoryStream();
            serializer.Serialize(indentedStream, response, true);

            stream.Seek(0, SeekOrigin.Begin);
            string serialized = new StreamReader(stream).ReadToEnd();
            TestContext.Out?.WriteLine("Serialized:");
            TestContext.Out?.WriteLine(serialized);

            size.Should().Be(serialized.Length);

            return serialized;
        }
        
        private class TestSingletonFactory<T> : SingletonFactory<T> where T : IRpcModule
        {
            private readonly IReadOnlyCollection<JsonConverter>? _converters;

            public TestSingletonFactory(T module, IReadOnlyCollection<JsonConverter>? converters) : base(module)
            {
                _converters = converters;
            }

            public override IReadOnlyCollection<JsonConverter> GetConverters() => _converters ?? base.GetConverters();
        }
    }


}
