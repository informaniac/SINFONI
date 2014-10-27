using System;
using WebSocket4Net;
using SuperSocket.ClientEngine;
using KIARA;

namespace WebSocketJSON
{
    public interface IWSJServerFactory
    {
        IWSJServer Construct(Action<Connection> onNewClient);
    }

    public class WSJServerFactory : IWSJServerFactory
    {
        public IWSJServer Construct(Action<Connection> onNewClient)
        {
            return new WSJServer(onNewClient);
        }
    }

    public interface IWSJFuncCallFactory
    {
        FuncCallBase Construct(string serviceName, string methodName);
    }

    public class WSJFuncCallFactory : IWSJFuncCallFactory
    {
        public FuncCallBase Construct(string serviceName, string methodName)
        {
            return new FuncCallBase(serviceName, methodName);
        }
    }

    public interface IWebSocketFactory
    {
        ISocket Construct(string uri);
    }

    public class WebSocketFactory : IWebSocketFactory
    {
        public ISocket Construct(string uri)
        {
            return new WebSocketSocketAdapter(uri);
        }
    }
}

