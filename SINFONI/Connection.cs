// This file is part of SINFONI.
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 3.0 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library.  If not, see <http://www.gnu.org/licenses/>.
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Net;
using SINFONI.Exceptions;
using System.Reflection;
using Dynamitey;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SINFONI
{
    /// <summary>
    /// Represents a generated function wrapper. It allows calling the function with arbitrary arguments. The returned
    /// call object is then used to wait for the performed call to return and to receive the result value from the
    /// remote service.
    /// </summary>
    /// <returns>An object representing a call.</returns>
    public delegate IClientFunctionCall ClientFunction(params object[] parameters);
    public delegate object GenericWrapper(params object[] arguments);


    /// <summary>
    /// This class represenents a connection to the remote end. It may be used to load new IDL definition files,
    /// generate callable remote function  wrappers and to register local functions as implementations for remote calls.
    /// </summary>
    public class Connection
    {

        public SinTD SinTD { get; internal set; }

        public Guid SessionID { get; internal set; }
        /// <summary>
        /// Raised when a connection is closed.
        /// </summary>
        public event EventHandler<ClosedEventArgs> Closed;

        internal bool Initialized = false;

        public Connection() { }

        public Connection(ITransportConnection transportConnection, IProtocol protocol)
        {
            this.SessionID = Guid.NewGuid();
            this.TransportConnection = transportConnection;
            this.Protocol = protocol;
            this.TransportConnection.Message += new EventHandler<TransportMessageEventArgs>(HandleMessage);
            this.TransportConnection.Closed += new EventHandler<ClosedEventArgs>((o, e) =>
            {
                if (this.Closed != null)
                    this.Closed(this, e);
            });
        }

        /// <summary>
        /// Closes the connection.
        /// </summary>
        public void Disconnect()
        {
            TransportConnection.Close();
            if (Closed != null)
                Closed(this, new ClosedEventArgs("Disconnect of Connection was requested"));
        }

        /// <summary>
        /// Convenient wrapper around GenerateFuncWrapper. Can be used to quickly create function wrapper and call it
        /// at once, e.g. <c>client["clientFunc"]("arg1", 42);</c>
        /// </summary>
        /// <param name="name">Function name.</param>
        /// <returns>Function wrapper.</returns>
        public ClientFunction this[string name]
        {            
            get
            {
                string[] serviceName = name.Split('.');
                return GenerateClientFunction(serviceName[0], serviceName[1]);
            }
        }

        /// <summary>
        /// Loads an IDL definition file from an URI or from the IDL contents specified in the server configuration
        /// into the connection.
        /// </summary>
        /// <param name="serverConfiguration">Configuration of the server that specifies the IDL</param>
        public void LoadIDL(Config serverConfiguration)
        {
            string contents = "";
            if (serverConfiguration.idlURL != null)
                contents = webClient.DownloadString(serverConfiguration.idlURL);
            else if (serverConfiguration.idlContents != null)
                contents = serverConfiguration.idlContents as string;
            else
                throw new MissingIDLException();
            try
            {
                SinTD = new IDLParser().ParseIDL(contents);
            }
            catch(Exception e)
            {
                Console.WriteLine("Failed to parse IDL from " + serverConfiguration.idlURL + ". Reason: " + e.Message);
            }
        }

        public void LoadLocalIDL(string IdlPath)
        {
            string idlContents = File.ReadAllText(IdlPath);
            SinTD = new IDLParser().ParseIDL(idlContents);
        }
        /// <summary>
        /// Handles an incoming message.
        /// </summary>
        public void HandleMessage(object sender, TransportMessageEventArgs e)
        {
            var handleMessageTask = Task.Factory.StartNew(() =>
            {
                HandleMessageAsync(e.Message);
            });
            handleMessageTask.ContinueWith(te =>
            {
                Console.WriteLine("[SINFONI.Connection] Incoming message could not be processed: {0}", te);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void HandleMessageAsync(object message)
        {

            IMessage receivedMessage = null;

            try
            {
                // Deserializes Message according to loaded protocol. As client agreed with server on respective protocol
                receivedMessage = Protocol.DeserializeMessage(message);
            }
            catch (Exception e)
            {
                #if DEBUG
                Console.WriteLine("An exception occurred while deserializing the message: " + e);
                #endif
            }

            if (!Initialized || deferredMessagesInQueue > 0)
            {
                #if DEBUG
                Console.WriteLine("[SINFONI.Connection] Not ready, deferring message until initialized");
                #endif
                lock (deferredMessages)
                {
                    deferredMessages.Enqueue(receivedMessage);
                    deferredMessagesInQueue++;
                }
            }
            else
            {
                ProcessMessage(receivedMessage);
            }
        }
        internal void FinishIntialization()
        {
            Initialized = true;
            while (deferredMessagesInQueue > 0)
            {
                ProcessDeferredMessage();
            }
        }

        private void ProcessDeferredMessage()
        {
            lock (deferredMessages)
            {
                #if DEBUG
                Console.WriteLine("[SINFONI.Connection] Processing deferred message, {0} left ", deferredMessagesInQueue);
                #endif
                IMessage queuedMessage = deferredMessages.Dequeue();
                deferredMessagesInQueue--;
                ProcessMessage(queuedMessage);
            }
        }
        private void ProcessMessage(IMessage message)
        {
            if (message == null)
            {
                #if DEBUG
                Console.WriteLine("A message was discarded because it deserialized to null");
                #endif
                return;
            }
            MessageType msgType = message.Type;
            if (msgType == MessageType.RESPONSE)
                HandleCallResponse(message);
            else if (msgType == MessageType.EXCEPTION)
                HandleCallError(message);
            else if (msgType == MessageType.REQUEST)
                HandleCall(message);
            else
                SendException(-1, "Unknown message type: " + msgType);
        }
        /// <summary>
        /// Is called when Connection receives a message that is identified as service call. Upon receiving a call,
        /// SINFONI will check whether the called service exists, what parameters it expects and which local function
        /// implements the service. If the service exists and the parameter types match, the local function called
        /// and a call-reply object with the result is sent back to the client
        /// </summary>
        /// <param name="callMessage">The deserialized message object that was received by the connection</param>
        private void HandleCall(IMessage callMessage)
        {
            int callID = callMessage.ID;
            string methodName = callMessage.MethodName;
            string[] serviceDescription = methodName.Split('.');

            Delegate nativeMethod = null;
            lock (registeredFunctions)
            {
                if (registeredFunctions.ContainsKey(methodName))
                    nativeMethod = registeredFunctions[methodName];
            }

            if (nativeMethod != null)
            {
                object[] parameters;
                try
                {
                    var args = callMessage.Parameters;
                    var callbacks = callMessage.Callbacks;
                    var paramInfo = new List<ParameterInfo>(nativeMethod.Method.GetParameters());
                    parameters = ConvertParameters(methodName, args, callbacks, paramInfo);
                }
                catch (Exception e)
                {
                    SendException(callID, e.Message);
                    return;
                }

                if (!IsOneWay(methodName))
                {
                    object returnValue = null;
                    object exception = null;
                    bool success = true;
                    try
                    {
                        // Super Evil Hack Here! Existing unit tests assume that WSJON serializes in a fixed format that
                        // originates from serializing the native types correctly. Also, the tests do not take into account
                        // any SinTD from any IDL. To make them work, we have to pretend that there is no ServiceRegistry
                        // maintaining any service description, but bypass type check and automatic SinTD Conversion
                        // by setting service Registry to null
                        if (SinTD == null)
                        {
                            returnValue = nativeMethod.DynamicInvoke(parameters);
                        }
                        else
                        {
                            ServiceFunctionDescription service = SinTD.SINFONIServices
                                .GetService(serviceDescription[0])
                                .GetServiceFunction(serviceDescription[1]);
                            returnValue = service.ReturnType.AssignValuesFromObject(nativeMethod.DynamicInvoke(parameters));
                        }
                    }
                    catch (Exception e)
                    {
                        exception = e;
                        success = false;
                    }
                    SendResponse(callID, nativeMethod, success, returnValue, exception);
                }
                else
                {
                    nativeMethod.DynamicInvoke(parameters);
                }
            }
            else
            {
                SendException(callID, "Method " + methodName + " is not registered");
                return;
            }
        }

        private object[] ConvertParameters(string methodName, List<object> args, List<int> callbacks, List<ParameterInfo> paramInfo)
        {
            object[] parameters = new object[paramInfo.Count];

            // Special handling for the first parameter if it's of type Connection.
            if (paramInfo.Count > 0 && paramInfo[0].ParameterType.Equals(typeof(Connection)))
            {
                parameters[0] = this;
                var otherParams = ConvertParameters(methodName, args, callbacks, paramInfo.GetRange(1, paramInfo.Count - 1));
                otherParams.CopyTo(parameters, 1);
                return parameters;
            }

            if (paramInfo.Count != args.Count)
            {
                throw new Exception("Incorrect number of arguments for a method. Expected: " +
                                              paramInfo.Count + ". Received: " + args.Count);
            }

            for (int i = 0; i < args.Count; i++)
            {
                // TODO: Handling of callbacks will change in the future and callbacks should be derived from the IDL.
                // Callbacks should not be transmitted as extra list, as it is unclear how list is treated in some protocols
                if (callbacks != null  && callbacks.Contains(i))
                {
                    if (paramInfo[i].ParameterType == typeof(ClientFunction))
                    {
                        parameters[i] = CreateFuncWrapperDelegate((string)args[i]);
                    }
                    else if (typeof(Delegate).IsAssignableFrom(paramInfo[i].ParameterType))
                    {
                        parameters[i] = CreateCustomDelegate((string)args[i], paramInfo[i].ParameterType);
                    }
                    else
                    {
                        throw new Exception("Parameter " + i + " is neither a delegate nor a FuncWrapper. " +
                                            "Cannot pass callback method in its place");
                    }
                }
                else
                {
                    // Super Evil Hack! See other super evil hack comment above
                    if (SinTD == null)
                    {
                        parameters[i] = Convert.ChangeType(args[i], paramInfo[i].ParameterType);
                    }
                    else
                    {
                        string[] service = methodName.Split('.');
                        SinTDType idlParameter = SinTD.SINFONIServices.GetService(service[0])
                            .GetServiceFunction(service[1]).Parameters.ElementAt(i).Value;
                        parameters[i] = idlParameter.AssignValuesToNativeType(args[i], paramInfo[i].ParameterType);
                    }

                }
            }

            return parameters;
        }

        private object CreateCustomDelegate(string funcName, Type delegateType)
        {
            Type retType = delegateType.GetMethod("Invoke").ReturnType;
            var genericWrapper = new GenericWrapper(arguments =>
            {
                if (retType == typeof(void))
                {
                    CallClientFunction(funcName, arguments);
                    // We do not wait here since SuperWebSocket doesn't process messages while the
                    // current thread is blocked. Waiting would bring the current client's thread
                    // into a deadlock.
                    return null;
                }
                else
                {
                    throw new NotImplementedException("We do not support callbacks with return " +
                        "value yet. This is because we cannot wait for a callback to complete. " +
                        "See more details here: https://redmine.viscenter.de/issues/1406.");

                    //object result = null;
                    //CallFunc(funcName, arguments)
                    //  .OnSuccess(delegate(JToken res) { result = res.ToObject(retType); })
                    //  .Wait();
                    //return result;
                }
            });

            return Dynamic.CoerceToDelegate(genericWrapper, delegateType);
        }

        private ClientFunction CreateFuncWrapperDelegate(string remoteCallbackUUID)
        {
            return (ClientFunction)delegate(object[] arguments)
            {
                return CallClientFunction(remoteCallbackUUID, arguments);
            };
        }

        private void HandleCallResponse(IMessage responseMessage)
        {
            int callID = responseMessage.ID;

            FuncCallBase completedCall = null;
            lock (activeCalls)
            {
                if (activeCalls.ContainsKey(callID))
                {
                    completedCall = activeCalls[callID];
                    activeCalls.Remove(callID);
                }
            }

            if (completedCall != null)
            {
                bool success = !responseMessage.IsException;
                object result = responseMessage.Result;
                if (success)
                    completedCall.HandleSuccess(result);
                else
                    completedCall.HandleException(new Exception(result as string));
            }
            else
            {
                SendException(-1, "Invalid callID: " + callID);
            }
        }

        private void HandleCallError(IMessage errorMessage)
        {
            int callID = errorMessage.ID;
            string reason = (string)errorMessage.Result;

            // Call error with callID = -1 means we've sent something that was not understood by other side or was
            // malformed. This probably means that protocols aren't incompatible or incorrectly implemented on either
            // side.
            if (callID == -1)
                throw new Exception(reason);

            FuncCallBase failedCall = null;
            lock (activeCalls)
            {
                if (activeCalls.ContainsKey(callID))
                {
                    failedCall = activeCalls[callID];
                    activeCalls.Remove(callID);
                }
            }

            if (failedCall != null)
                failedCall.HandleError(reason);
            else
                Console.WriteLine("One Way Call to " + errorMessage.MethodName
                    + " returned Exception: " + errorMessage.Result);
        }

        private void SendResponse(int callID, Delegate nativeMethod, bool success, object retValue, object exception)
        {
            MessageBase responseMessage = new MessageBase();
            responseMessage.Type = MessageType.RESPONSE;
            responseMessage.ID = callID;
            responseMessage.IsException = !success;
            if (!success)
                responseMessage.Result = exception;
            else if (nativeMethod.Method.ReturnType != typeof(void))
                responseMessage.Result = retValue;
            SendMessage(responseMessage);
        }

        private void SendException(int callID, string reason)
        {
            MessageBase errorMessage = new MessageBase();
            errorMessage.Type = MessageType.EXCEPTION;
            errorMessage.IsException = true;
            errorMessage.ID = callID;
            errorMessage.Result = reason;
            SendMessage(errorMessage);
        }

        /// <summary>
        /// Generates a client function for the <paramref name="funcName"/>. Optional <paramref name="typeMapping"/> string
        /// may be used to specify data omission and reordering options.
        /// </summary>
        /// <returns>The generated client function.</returns>
        /// <param name="serviceName">Service that contains the wrapped function.</param>
        /// <param name="functionName">Name of the function that should be wrapped.</param>
        public virtual ClientFunction GenerateClientFunction(string serviceName, string functionName)
        {
            if (!SinTD.SINFONIServices.ContainsService(serviceName))
                throw new ServiceNotRegisteredException(serviceName);

            var service = SinTD.SINFONIServices.GetService(serviceName);

            if (!service.ContainsServiceFunction(functionName))
                throw new ServiceNotRegisteredException(functionName);

            return (ClientFunction)delegate(object[] parameters)
            {
                SINFONIService registeredService = SinTD.SINFONIServices.GetService(serviceName);
                ServiceFunctionDescription registeredServiceFunction = registeredService.GetServiceFunction(functionName);

                if (!registeredServiceFunction.CanBeCalledWithParameters(parameters))
                {
                    throw new ParameterMismatchException(
                        "Could not call Service Function " + serviceName + "." + functionName
                            + ". The provided parameters can not be mapped to the parameters specified in the IDL.");
                }
                object[] callParameters = new object[parameters.Length];
                for (var i = 0; i < parameters.Length; i++)
                {
                    SinTDType expectedParameterType = registeredServiceFunction.Parameters.ElementAt(i).Value;
                    callParameters[i] = expectedParameterType.AssignValuesFromObject(parameters[i]);
                }
                return CallClientFunction(serviceName + "." + functionName, callParameters);
            };
        }

        /// <summary>
        /// Registers a local <paramref name="handler"/> as an implementation for the <paramref name="funcName"/>.
        /// Optional <paramref name="typeMapping"/> string can be used to specify data omission and reordering options.
        /// </summary>
        /// <param name="funcName">Name of the implemented function.</param>
        /// <param name="handler">Handler to be invoked upon remote call.</param>
        /// <param name="typeMapping">Type mapping string.</param>
        public void RegisterFuncImplementation(string funcName, Delegate handler, string typeMapping = "")
        {
            // TODO: implement type mapping and add respective tests
            RegisterHandler(funcName, handler);
        }

        /// <summary>
        /// Sets some property of the connection. May be used by subclasses to allow clients to configure them.
        /// </summary>
        /// <returns><c>true</c>, if property is supported and accepts given value, <c>false</c> otherwise.</returns>
        /// <param name="name">Name of the property.</param>
        /// <param name="value">Value to be set.</param>
        public virtual bool SetProperty(string name, object value)
        {
            return false;
        }

        /// <summary>
        /// Returns some property of the connection. May be used by subclasses to allow clients obtain some information
        /// about the connection.
        /// </summary>
        /// <returns><c>true</c>, if property is supported, <c>false</c> otherwise.</returns>
        /// <param name="name">Name of the property.</param>
        /// <param name="value">Value to be returned.</param>
        public virtual bool GetProperty(string name, out object value)
        {
            value = null;
            return false;
        }

        /// <summary>
        /// Calls a function with a given name and arguments on the remote end.
        /// </summary>
        /// <param name="funcName">Function name.</param>
        /// <param name="args">Argunents.</param>
        /// <returns>Object representing remote call.</returns>
        protected virtual IClientFunctionCall CallClientFunction(string funcName, params object[] args)
        {
            int callID = getValidCallID();

            // Register delegates as callbacks. Pass their registered names instead.
            List<int> callbacks;
            List<object> convertedArgs = convertCallbackArguments(args, out callbacks);

            IMessage callMessage = createRequestMessage(callID, funcName, callbacks, convertedArgs);

            string[] serviceDescription = funcName.Split('.');

            // Usually, a function called via CallClientFunction is parsed from the SINFONI IDL and of the
            // form serviceName.functionName. However, in some cases (e.g. twisted Unit Tests), functions may be
            // created locally, only having a GUID as function name. In this case, we appen "LOCAL" as service name
            // to mark the function as locally created
            if (serviceDescription.Length < 2)
            {
                string[] localService = new string[2];
                localService[0] = "LOCAL";
                localService[1] = serviceDescription[0];
                serviceDescription = localService;
            }

            FuncCallBase callObj = new FuncCallBase(serviceDescription[0], serviceDescription[1], this);
            if (!IsOneWay(funcName))
            {
                // It is important to add an active call to the list before sending it, otherwise we may end up
                // receiving call-reply before this happens, which will trigger unnecessary call-error and crash the
                // other end.
                lock (activeCalls)
                    activeCalls.Add(callID, callObj);
            }

            SendMessage(callMessage);

            return callObj;
        }

        private bool IsOneWay(string methodName)
        {
            if(oneWayFunctions.ContainsKey(methodName))
            {
                return oneWayFunctions[methodName];
            }
            else
            {
                return CheckIfOneWay(methodName);
            }
        }

        private bool CheckIfOneWay(string methodName)
        {
            var serviceDescription = methodName.Split('.');
            oneWayFunctions[methodName] = SinTD.SINFONIServices
                .GetService(serviceDescription[0])
                .GetServiceFunction(serviceDescription[1])
                .ReturnType.Name == "void";
            return oneWayFunctions[methodName];
        }

        private int getValidCallID()
        {
            lock (nextCallIDLock)
            {
                return nextCallID++;
            }
        }

        private IMessage createRequestMessage(int callID, string name, List<int> callbacks, List<object> convertedArgs)
        {
            MessageBase requestMessage = new MessageBase();
            requestMessage.Type = MessageType.REQUEST;
            requestMessage.ID = callID;
            requestMessage.MethodName = name;
            requestMessage.Parameters = convertedArgs;
            requestMessage.Callbacks = callbacks;

            return requestMessage;
        }

        private void SendMessage(IMessage message)
        {
            var serializedMessage = Protocol.SerializeMessage(message);
            TransportConnection.Send(serializedMessage);
        }

        private List<object> convertCallbackArguments(object[] args, out List<int> callbacks)
        {
            callbacks = createCallbacksFromArguments(args);

            List<object> convertedArgs = new List<object>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is Delegate)
                {
                    var arg = args[i] as Delegate;
                    string callbackGuid = null;
                    lock (registeredCallbacks)
                        callbackGuid = registeredCallbacks[arg];
                    convertedArgs.Add(callbackGuid);
                }
                else
                {
                    convertedArgs.Add(args[i]);
                }
            }
            return convertedArgs;
        }

        private List<int> createCallbacksFromArguments(object[] args)
        {
            List<int> callbacks = new List<int>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is Delegate)
                {
                    var arg = args[i] as Delegate;

                    string callbackGuid = null;
                    lock (registeredCallbacks)
                    {
                        if (!registeredCallbacks.ContainsKey(arg))
                        {
                            callbackGuid = Guid.NewGuid().ToString();
                            registeredCallbacks[arg] = callbackGuid;
                        }
                        else
                        {
                            callbackGuid = registeredCallbacks[arg];
                        }
                    }

                    lock (registeredFunctions)
                        registeredFunctions[callbackGuid] = arg;

                    callbacks.Add(i);
                }
            }
            return callbacks;
        }

        /// <summary>
        /// Registers a handler to be invoked when a function with given name is invoked remotely.
        /// </summary>
        /// <param name="funcName">Function name.</param>
        /// <param name="handler">Handler delegate.</param>
        protected void RegisterHandler(string funcName, Delegate handler)
        {
            lock (registeredFunctions)
                registeredFunctions[funcName] = handler;
        }

        internal IWebClient webClient = new WebClientWrapper();

        private object nextCallIDLock = new object();
        private int nextCallID = 0;
        private Dictionary<int, FuncCallBase> activeCalls = new Dictionary<int, FuncCallBase>();
        private Dictionary<string, Delegate> registeredFunctions = new Dictionary<string, Delegate>();
        private Dictionary<Delegate, string> registeredCallbacks = new Dictionary<Delegate, string>();
        private Dictionary<string, bool> oneWayFunctions = new Dictionary<string, bool>();
        protected ITransportConnection TransportConnection;
        protected IProtocol Protocol;
        private Queue<IMessage> deferredMessages = new Queue<IMessage>();
        private int deferredMessagesInQueue = 0;
    }
}
