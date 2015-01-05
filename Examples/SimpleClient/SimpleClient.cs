﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KIARA;
using KIARA.Protocols.JsonRPC;
using KIARA.Transport.WebSocketTransport;

namespace SimpleClient
{
    public class SimpleClient
    {
        public SimpleClient()
        {
            IProtocol jsonRpcProtocol = new JsonRpcProtocol();
            ITransport webSocketTransport = new WebSocketTransport();

            ProtocolRegistry.Instance.RegisterProtocol("jsonrpc", jsonRpcProtocol);
            TransportRegistry.Instance.RegisterTransport("ws", webSocketTransport);
            RemoteService = ServiceFactory.Discover("http://localhost:8080/service/");
            RemoteService.OnConnected += new Connected(HandleConnected);

            Console.ReadKey();
        }

        private void HandleConnected(Connection connection)
        {
            string idlURI = RemoteService.Context.ServerConfiguarion.idlURL;
            connection.LoadIDL(idlURI);
            AddVectors = connection["example.addVectors"];
            callAddVectors();
        }

        private void callAddVectors()
        {
            ClientVector a = new ClientVector { x = 1, y = 2, z = 3 };
            ClientVector b = new ClientVector { x = 10, y = 20, z = 30 };
            IClientFunctionCall addCall = AddVectors(a, b);
            addCall.OnSuccess<ClientVector>((Action<ClientVector>)onResultReturned);
            addCall.OnFailure((Action)onFailureReturned);
        }

        private void onResultReturned(ClientVector result)
        {
            Console.WriteLine("Received some Result: ["
                + result.x + ","
                + result.y + ","
                + result.z + "]");
        }

        private void onFailureReturned()
        {
            Console.WriteLine("Error");
        }

        IServiceWrapper RemoteService;
        ClientFunction AddVectors;
    }
}
