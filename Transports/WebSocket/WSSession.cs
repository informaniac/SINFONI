using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SuperWebSocket;
using Newtonsoft.Json;
using System.Reflection;
using Dynamitey;
using System.Runtime.InteropServices;
using WebSocket4Net;
using NLog;
using KIARA;

namespace KIARA.Transport.WebSocketTransport
{
    /// <summary>
    /// WebSocketJSON session implementation. Contains Connection adapter for KIARA.
    /// </summary>
    public class WSSession : WebSocketSession<WSSession>
    {
        public event EventHandler Closed;
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        public void HandleClosed(string reason)
        {
            if (Closed != null)
                Closed(this, new ClosedEventArgs(reason));

            if (!reason.Equals("ClientClosing"))
                logger.Warn("Connection closed: " + reason);
        }

        public void HandleMessageReceived(string message)
        {
            if (MessageReceived != null)
                MessageReceived(this, new MessageReceivedEventArgs(message));
        }

        private static Logger logger = LogManager.GetCurrentClassLogger();
    }
}

