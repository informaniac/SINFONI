﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Newtonsoft.Json;
using System.Net;
using Newtonsoft.Json.Linq;

namespace KIARA
{
    #region JSON Config structure
    public struct Server
    {
        public string services;
        public JToken protocol;
        public JToken transport;
    }

    public struct Config
    {
        public string info;
        public string idlURL;        
        public List<Server> servers;
    }
    #endregion

    /// <summary>
    /// Represents an independent context for KIARA.
    /// </summary>
    public class Context
    {
        public static Context DefaultContext = new Context();

        public Config ServerConfiguarion { get; private set; }
        public void Initialize(string hint)
        {
        }

        /// <summary>
        /// Opens a connection to a server specified in the config file retrived from <paramref name="configURI"/>.
        /// Fragment part of the <paramref name="configURI"/> may be used to select the server by its index, e.g.
        /// <c>"http://www.example.org/config.json#3"</c>. If no fragment is provided, or index is invalid, first server
        /// with supported protocol is chosen. Upon connection <paramref name="onConnected"/> is called with the
        /// constructed <see cref="KIARAPluginInitializer.Connection"/> object.
        /// </summary>
        /// <param name="configURI">
        /// URI where config is to be found. Data URIs starting with <c>"data:text/json;base64,"</c> are supported.
        /// </param>
        /// <param name="onConnected">Handler to be invoked when connection is established.</param>
        public void OpenConnection(string configURI, Action<Connection> onConnected)
        {
            string fragment = "";
            this.ServerConfiguarion = RetrieveConfig(configURI, out fragment);                       
            Server server = SelectServer(fragment, this.ServerConfiguarion);

            string protocolName = server.protocol["name"].ToString();
            IConnectionFactory connectionFactory = protocolRegistry.GetConnectionFactory(protocolName);
            connectionFactory.OpenConnection(server, this, onConnected);
        }

        /// <summary>
        /// Creates a server specified in the config file retrieved from <paramref name="configURI"/>. Fragment part of
        /// the <paramref name="configURI"/> may be used to select the server by its index, e.g.
        /// <c>"http://www.example.org/config.json#3"</c>. If no fragment is provided, or index is invalid, first server
        /// with supported protocol is chosen. For each connected client <paramref name="onNewClient"/> is called with
        /// constructed <see cref="KIARAPluginInitializer.Connection"/> object.
        /// </summary>
        /// <remarks>
        /// Note that <paramref name="onNewClient"/> may be executed on a different thread than the one you are calling
        /// from, depending on the implementation of the protocol specified in the config file.
        /// </remarks>
        /// <param name="configURI">
        /// URI where config is to be found. Data URIs starting with <c>"data:text/json;base64,"</c> are supported.
        /// </param>
        /// <param name="onNewClient">Handler to be invoked for each new client.</param>
        public void StartServer(string configURI, Action<Connection> onNewClient)
        {
            string fragment = "";
            Config config = RetrieveConfig(configURI, out fragment);
            IDLParser.Instance.ParseIDLFromUri(config.idlURL);
            Server server = SelectServer(fragment, config);
            string protocolName = server.protocol["name"].ToString();
            // ConnectionFactory -> TransportConnectionFactory
            // Somewhere get Protocol from !!

            // ITransportConnectionFactory transportConnectionFactory = transportRegistry.GetTransportConnectionFactory(transportName)
            // transportConnectionListener = transportConnectionFactory.startConnectionListener(uri);
            // transportConnectionListener.OnNewSessionConnected = (ITransportConnection transportConnection) => {
            //      Connection newConnection = newConnection(transportConnection, protocol);
            //      onNewClient(newConnection);
            // }
            IConnectionFactory connectionFactory = protocolRegistry.GetConnectionFactory(protocolName);
            connectionFactory.StartServer(server, this, onNewClient);
        }

        /// <summary>
        /// Retrieves and parses config file from a given URI.
        /// </summary>
        /// <param name="configURI">URI pointing to the config file.</param>
        /// <param name="fragment">Fragment part of the URI.</param>
        /// <returns>Parsed config.</returns>
        public Config RetrieveConfig(string configURI, out string fragment)
        {
            // Extract fragment.
            int hashIndex = configURI.IndexOf("#");
            if (hashIndex != -1) {
                fragment = configURI.Substring(hashIndex + 1);
                configURI = configURI.Substring(0, hashIndex);
            } else {
                fragment = "";
            }

            // Retrieve config content.
            string configContent;
            if (configURI.StartsWith("data:text/json;base64,")) {
                string base64Content = configURI.Substring(22);
                byte[] byteData = System.Convert.FromBase64String(base64Content);
                configContent = System.Text.Encoding.ASCII.GetString(byteData);
            } else {
                configContent = webClient.DownloadString(configURI);
            }

            // Parse the config.
            return JsonConvert.DeserializeObject<Config>(configContent);
        }

        private bool IsServerProtocolSupported(Server server) {
            if (server.protocol == null)
                return false;

            JToken protocolName = server.protocol.SelectToken("name");
            if (protocolName == null)
                return false;

            return protocolRegistry.IsRegistered(protocolName.ToString());
        }

        private Server SelectServer(string fragment, Config config)
        {
            if (config.servers == null)
                throw new Error(ErrorCode.INIT_ERROR, "Configuration file contains no servers.");
            
            int serverNum = -1;
            if (!Int32.TryParse(fragment, out serverNum) || serverNum < 0 || serverNum >= config.servers.Count ||
                !IsServerProtocolSupported(config.servers[serverNum])) {
                serverNum = config.servers.FindIndex(s => IsServerProtocolSupported(s));
            }

            if (serverNum == -1)
                throw new Error(ErrorCode.INIT_ERROR, "Found no server with compatible protocol.");

            return config.servers[serverNum];
        }

        internal IProtocolRegistry protocolRegistry = ProtocolRegistry.Instance;
        internal IWebClient webClient = new WebClientWrapper();
        // TODO: Diese Liste soll alle durch StartService gestarteten SERVICES enthalten. Die CONFIG oben wird dann aus der Liste dieser
        // SERVICES erstellt.
        // StartService benutzt dann KEINE CONFIG mehr, sondern wird direkt durch ANGABE VON TRANSPORT, PROTOCOL und PFAD im Code definiert
        internal List<Service> registeredServices = new List<Service>();
    }
}
