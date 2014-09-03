// **********************************************************************
//
// Copyright (c) 2003-2014 ZeroC, Inc. All rights reserved.
//
// This copy of Ice is licensed to you under the terms described in the
// ICE_LICENSE file included in this distribution.
//
// **********************************************************************

namespace Ice
{
    using System.Threading;
    using System.Collections.Generic;
    using System.Diagnostics;

    ///
    /// <summary>
    /// Callback that requires the application to down-cast the proxy.
    /// </summary>
    ///
    public delegate void AsyncCallback(AsyncResult r);

    ///
    /// <summary>
    /// Callback for the successful completion of an operation
    /// that returns no data.
    /// </summary>
    ///
    public delegate void OnewayCallback();

    ///
    /// <summary>
    /// Callback for the successful completion of an operation
    /// that returns no data.
    /// </summary>
    ///
    public delegate void SentCallback(bool sentSynchronously);

    ///
    /// <summary>
    /// Called when an invocation raises an exception.
    /// </summary>
    ///
    public delegate void ExceptionCallback(Ice.Exception ex);

    ///
    /// <summary>
    /// <!-- TODO -->
    /// </summary>
    public interface AsyncResult : System.IAsyncResult
    {
        Ice.Communicator getCommunicator();

        Ice.Connection getConnection();

        ObjectPrx getProxy();

        bool isCompleted_();
        void waitForCompleted();

        bool isSent();
        void waitForSent();

        void throwLocalException();

        bool sentSynchronously();

        string getOperation();

        AsyncResult whenSent(Ice.AsyncCallback cb);
        AsyncResult whenSent(Ice.SentCallback cb);

        AsyncResult whenCompleted(Ice.ExceptionCallback excb);
    }

    public interface AsyncResult<T> : AsyncResult
    {
        AsyncResult<T> whenCompleted(T cb, Ice.ExceptionCallback excb);

        new AsyncResult<T> whenCompleted(Ice.ExceptionCallback excb);
        new AsyncResult<T> whenSent(Ice.SentCallback cb);
    }
}

namespace IceInternal
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;

    public delegate void ProxyTwowayCallback<T>(Ice.AsyncResult result, T cb, Ice.ExceptionCallback excb);
    public delegate void ProxyOnewayCallback<T>(T cb);

    //
    // This interface is used by the connection to handle OutgoingAsync
    // and BatchOutgoingAsync messages.
    //
    public interface OutgoingAsyncMessageCallback
    {
        //
        // Called by the request handler to send the request over the connection.
        //
        bool send(Ice.ConnectionI connection, bool compress, bool response, out Ice.AsyncCallback sentCallback);

        bool invokeCollocated(CollocatedRequestHandler handler, out Ice.AsyncCallback sentCallback);
        
        //
        // Called by the connection when the message is confirmed sent. The connection is locked
        // when this is called so this method can call the sent callback. Instead, this method
        // returns true if there's a sent callback and false otherwise. If true is returned, the
        // connection will call the __sent() method bellow (which in turn should call the sent
        // callback).
        //
        Ice.AsyncCallback sent();

        //
        // Called by the connection to call the user sent callback.
        //
        void invokeSent(Ice.AsyncCallback cb);

        //
        // Called by the connection when the request failed.
        //
        void finished(Ice.Exception ex);

        //
        // Helper to dispatch invocation timeout.
        //
        void dispatchInvocationCancel(Ice.LocalException ex, ThreadPool threadPool, Ice.Connection connection);
    }

    abstract public class OutgoingAsyncBase : Ice.AsyncResult
    {
        public virtual Ice.Communicator getCommunicator()
        {
            return communicator_;
        }

        public virtual Ice.Connection getConnection()
        {
            return null;
        }

        public virtual Ice.ObjectPrx getProxy()
        {
            return null;
        }

        public bool isCompleted_()
        {
            lock(monitor_)
            {
                return (state_ & StateDone) != 0;
            }
        }

        public void waitForCompleted()
        {
            lock(monitor_)
            {
                while((state_ & StateDone) == 0)
                {
                    System.Threading.Monitor.Wait(monitor_);
                }
            }
        }

        public bool isSent()
        {
            lock(monitor_)
            {
                return (state_ & StateSent) != 0;
            }
        }

        public void waitForSent()
        {
            lock(monitor_)
            {
                while((state_ & StateSent) == 0 && exception_ == null)
                {
                    System.Threading.Monitor.Wait(monitor_);
                }
            }
        }

        public void throwLocalException()
        {
            lock(monitor_)
            {
                if(exception_ != null)
                {
                    throw exception_;
                }
            }
        }

        public bool sentSynchronously()
        {
            return sentSynchronously_; // No lock needed, immutable once invoke() is called
        }

        //
        // Implementation of System.IAsyncResult properties
        //

        public bool IsCompleted
        {
            get
            {
                return isCompleted_();
            }
        }

        public bool CompletedSynchronously
        {
            get
            {
                if(getProxy() != null && getProxy().ice_isTwoway())
                {
                    return false;
                }
                return sentSynchronously_;
            }
        }

        public object AsyncState
        {
            get
            {
                return _cookie; // No lock needed, cookie is immutable.
            }
        }

        public WaitHandle AsyncWaitHandle
        {
            get
            {
                lock(monitor_)
                {
                    if(waitHandle_ == null)
                    {
#if SILVERLIGHT
                        waitHandle_ = new ManualResetEvent(false);
#else
                        waitHandle_ = new EventWaitHandle(false, EventResetMode.ManualReset);
#endif
                    }
                    if((state_ & StateDone) != 0)
                    {
                        waitHandle_.Set();
                    }
                    return waitHandle_;
                }
            }
        }

        public Ice.AsyncResult whenSent(Ice.AsyncCallback cb)
        {
            lock(monitor_)
            {
                if(cb == null)
                {
                    throw new System.ArgumentException("callback is null");
                }
                if(sentCallback_ != null)
                {
                    throw new System.ArgumentException("sent callback already set");
                }
                sentCallback_ = cb;
                if((state_ & StateSent) == 0)
                {
                    return this;
                }
            }

            if(sentSynchronously_)
            {
                try
                {
                    sentCallback_(this);
                }
                catch(System.Exception ex)
                {
                    warning(ex);
                }
            }
            else
            {
                instance_.clientThreadPool().dispatch(() =>
                {
                    try
                    {
                        sentCallback_(this);
                    }
                    catch(System.Exception ex)
                    {
                        warning(ex);
                    }
                }, cachedConnection_);
            }
            return this;
        }

        public Ice.AsyncResult whenSent(Ice.SentCallback cb)
        {
            lock(monitor_)
            {
                if(cb == null)
                {
                    throw new System.ArgumentException("callback is null");
                }
                if(sentCallback_ != null)
                {
                    throw new System.ArgumentException("sent callback already set");
                }
                sentCallback_ = (Ice.AsyncResult result) =>
                    {
                        cb(result.sentSynchronously());
                    };
                if((state_ & StateSent) == 0)
                {
                    return this;
                }
            }

            if(sentSynchronously_)
            {
                try
                {
                    cb(true);
                }
                catch(System.Exception ex)
                {
                    warning(ex);
                }
            }
            else
            {
                instance_.clientThreadPool().dispatch(() =>
                {
                    try
                    {
                        cb(false);
                    }
                    catch(System.Exception ex)
                    {
                        warning(ex);
                    }
                }, cachedConnection_);
            }
            return this;
        }

        public Ice.AsyncResult whenCompletedWithAsyncCallback(Ice.AsyncCallback cb)
        {
            lock(monitor_)
            {
                setCompletedCallback(cb);
                if((state_ & StateDone) == 0)
                {
                    return this;
                }
                else if((getProxy() == null || getProxy().ice_isOneway()) && exception_ == null)
                {
                    return this;
                }
            }

            instance_.clientThreadPool().dispatch(() =>
            {
                try
                {
                    cb(this);
                }
                catch(System.Exception ex)
                {
                    warning(ex);
                }
            }, cachedConnection_);
            return this;
        }

        public Ice.AsyncResult whenCompleted(Ice.ExceptionCallback cb)
        {
            lock(monitor_)
            {
                if(cb == null)
                {
                    throw new System.ArgumentException("callback is null");
                }
                setCompletedCallback(getCompletedCallback());
                exceptionCallback_ = cb;
                if((state_ & StateDone) == 0)
                {
                    return this;
                }
                else if((getProxy() == null || getProxy().ice_isOneway()) && exception_ == null)
                {
                    return this;
                }
            }

            instance_.clientThreadPool().dispatch(() =>
                {
                    try
                    {
                        completedCallback_(this);
                    }
                    catch(System.Exception ex)
                    {
                        warning(ex);
                    }
                }, cachedConnection_);
            return this;
        }

        public string getOperation()
        {
            return operation_;
        }

        public IceInternal.BasicStream ostr__
        {
            get
            {
                return os_;
            }
        }

        public bool wait()
        {
            lock(monitor_)
            {
                if((state_ & StateEndCalled) != 0)
                {
                    throw new System.ArgumentException("end_ method called more than once");
                }
                state_ |= StateEndCalled;
                while((state_ & StateDone) == 0)
                {
                    System.Threading.Monitor.Wait(monitor_);
                }
                if(exception_ != null)
                {
                    throw exception_;
                }
                return (state_ & StateOK) != 0;
            }
        }

        public void throwUserException()
        {
            try
            {
                is_.startReadEncaps();
                is_.throwException(null);
            }
            catch(Ice.UserException)
            {
                is_.endReadEncaps();
                throw;
            }
        }

        virtual public void invokeExceptionAsync(Ice.Exception ex)
        {
            //
            // This is called when it's not safe to call the exception callback synchronously
            // from this thread. Instead the exception callback is called asynchronously from
            // the client thread pool.
            //
            try
            {
                instance_.clientThreadPool().dispatch(() =>
                    {
                        invokeException(ex);
                    }, cachedConnection_);
            }
            catch(Ice.CommunicatorDestroyedException)
            {
                throw; // CommunicatorDestroyedException is the only exception that can propagate directly.
            }
        }

        virtual public void attachRemoteObserver(Ice.ConnectionInfo info, Ice.Endpoint endpt, int requestId, int sz)
        {
            if(observer_ != null)
            {
                childObserver_ = observer_.getRemoteObserver(info, endpt, requestId, sz);
                if(childObserver_ != null)
                {
                    childObserver_.attach();
                }
            }
        }

        virtual public void attachCollocatedObserver(Ice.ObjectAdapter adapter, int requestId)
        {
            if(observer_ != null)
            {
                childObserver_ = observer_.getCollocatedObserver(adapter, 
                                                                 requestId, 
                                                                 os_.size() - IceInternal.Protocol.headerSize - 4);
                if(childObserver_ != null)
                {
                    childObserver_.attach();
                }
            }
        }

        public void invokeSentAsync(Ice.AsyncCallback callback)
        {
            //
            // This is called when it's not safe to call the exception callback synchronously
            // from this thread. Instead the exception callback is called asynchronously from
            // the client thread pool.
            //
            if(callback != null)
            {
                try
                {
                    instance_.clientThreadPool().dispatch(() =>
                        {
                            invokeSent(callback);
                        }, cachedConnection_);
                }
                catch(Ice.CommunicatorDestroyedException)
                {
                }
            }
        }

        public static void check(OutgoingAsyncBase r, Ice.ObjectPrx prx, string operation)
        {
            check(r, operation);
            if(r.getProxy() != prx)
            {
                throw new System.ArgumentException("Proxy for call to end_" + operation +
                                                   " does not match proxy that was used to call corresponding begin_" +
                                                   operation + " method");
            }
        }

        public static void check(OutgoingAsyncBase r, Ice.Connection con, string operation)
        {
            check(r, operation);
            if(r.getConnection() != con)
            {
                throw new System.ArgumentException("Connection for call to end_" + operation +
                                                   " does not match connection that was used to call " +
                                                   "corresponding begin_" + operation + " method");
            }
        }

        public static void check(OutgoingAsyncBase r, Ice.Communicator com, string operation)
        {
            check(r, operation);
            if(r.getCommunicator() != com)
            {
                throw new System.ArgumentException("Communicator for call to end_" + operation +
                                                   " does not match communicator that was used to call " +
                                                   "corresponding begin_" + operation + " method");
            }
        }

        protected static void check(OutgoingAsyncBase r, string operation)
        {
            if(r == null)
            {
                throw new System.ArgumentException("AsyncResult == null");
            }
            if(r.getOperation() != operation)
            {
                throw new System.ArgumentException("Incorrect operation for end_" + operation + " method: " +
                                                   r.getOperation());
            }
        }

        protected OutgoingAsyncBase(Ice.Communicator communicator, Instance instance, string op, object cookie)
        {
            communicator_ = communicator;
            instance_ = instance;
            operation_ = op;
            // Lazy initialized when response is received.
            //is_ = new IceInternal.BasicStream(instance);
            os_ = new IceInternal.BasicStream(instance, Ice.Util.currentProtocolEncoding);
            state_ = 0;
            sentSynchronously_ = false;
            exception_ = null;
            _cookie = cookie;
        }

        protected OutgoingAsyncBase(Ice.Communicator communicator, Instance instance, string op, object cookie, BasicStream iss, BasicStream os)
        {
            communicator_ = communicator;
            instance_ = instance;
            operation_ = op;
            is_ = iss;
            os_ = os;
            state_ = 0;
            sentSynchronously_ = false;
            exception_ = null;
            _cookie = cookie;
        }

        protected void setCompletedCallback(Ice.AsyncCallback cb)
        {
            if(cb == null)
            {
                throw new System.ArgumentException("callback is null");
            }
            if(completedCallback_ != null)
            {
                throw new System.ArgumentException("completed callback already set");
            }
            completedCallback_ = cb;
        }

        protected void invokeSent(Ice.AsyncCallback cb)
        {
            //
            // Note: no need to change the state_ here, specializations are responsible for
            // changing the state.
            //

            if(cb != null)
            {
                try
                {
                    cb(this);
                }
                catch(System.Exception ex)
                {
                    warning(ex);
                }
            }

            if(observer_ != null)
            {
                Ice.ObjectPrx proxy = getProxy();
                if(proxy == null || !proxy.ice_isTwoway())
                {
                    observer_.detach();
                    observer_ = null;
                }
            }
        }

        protected void invokeCompleted(Ice.AsyncCallback cb)
        {
            //
            // Note: no need to change the state_ here, specializations are responsible for
            // changing the state.
            //

            if(cb != null)
            {
                try
                {
                    cb(this);
                }
                catch(System.Exception ex)
                {
                    warning(ex);
                }
            }

            if(observer_ != null)
            {
                observer_.detach();
                observer_ = null;
            }
        }

        protected void invokeException(Ice.Exception ex)
        {
            Ice.AsyncCallback cb;
            lock(monitor_)
            {
                state_ |= StateDone;
                os_.resize(0, false); // Clear buffer now, instead of waiting for AsyncResult deallocation
                exception_ = ex;
                System.Threading.Monitor.PulseAll(monitor_);
                if(waitHandle_ != null)
                {
                    waitHandle_.Set();
                }
                cb = completedCallback_;
            }

            if(cb != null)
            {
                try
                {
                    cb(this);
                }
                catch(System.Exception exc)
                {
                    warning(exc);
                }
            }

            if(observer_ != null)
            {
                observer_.detach();
                observer_ = null;
            }
        }

        protected virtual Ice.AsyncCallback getCompletedCallback()
        {
            return completed;
        }

        private void completed(Ice.AsyncResult result)
        {
            Debug.Assert(exceptionCallback_ != null);
            try
            {
                ((OutgoingAsyncBase)result).wait();
            }
            catch(Ice.Exception ex)
            {
                exceptionCallback_(ex);
                return;
            }
        }

        protected void runTimerTask__()
        {
            IceInternal.RequestHandler handler;
            lock(monitor_)
            {

                handler = timeoutRequestHandler_; 
                timeoutRequestHandler_ = null;
            }
            
            if(handler != null)
            {
                handler.asyncRequestCanceled((OutgoingAsyncMessageCallback)this, new Ice.InvocationTimeoutException());
            }
        }

        protected void warning(System.Exception ex)
        {
            if(instance_.initializationData().properties.getPropertyAsIntWithDefault("Ice.Warn.AMICallback", 1) > 0)
            {
                instance_.initializationData().logger.warning("exception raised by AMI callback:\n" + ex);
            }
        }

        protected Ice.Communicator communicator_;
        protected IceInternal.Instance instance_;
        protected string operation_;
        protected Ice.Connection cachedConnection_;

        protected readonly object monitor_ = new object();
        protected IceInternal.BasicStream is_;
        protected IceInternal.BasicStream os_;

        protected IceInternal.RequestHandler timeoutRequestHandler_;

        protected const int StateOK = 0x1;
        protected const int StateDone = 0x2;
        protected const int StateSent = 0x4;
        protected const int StateEndCalled = 0x8;
        protected const int StateCachedBuffers = 0x10;

        protected int state_;
        protected bool sentSynchronously_;
        //
        // If true this AMI request is being used for a generated synchronous invocation.
        //
        protected bool synchronous_;
        protected Ice.Exception exception_;
        protected EventWaitHandle waitHandle_;

        protected Ice.Instrumentation.InvocationObserver observer_;
        protected Ice.Instrumentation.ChildInvocationObserver childObserver_;

        protected Ice.AsyncCallback completedCallback_;
        protected Ice.AsyncCallback sentCallback_;
        protected Ice.ExceptionCallback exceptionCallback_;

        private object _cookie;
    }

    abstract public class OutgoingAsync : OutgoingAsyncBase, OutgoingAsyncMessageCallback, TimerTask
    {
        public OutgoingAsync(Ice.ObjectPrxHelperBase prx, string operation, object cookie) :
            base(prx.ice_getCommunicator(), prx.reference__().getInstance(), operation, cookie)
        {
            proxy_ = prx;
            _encoding = Protocol.getCompatibleEncoding(proxy_.reference__().getEncoding());
        }

        public OutgoingAsync(Ice.ObjectPrxHelperBase prx, string operation, object cookie, BasicStream iss, BasicStream os) :
            base(prx.ice_getCommunicator(), prx.reference__().getInstance(), operation, cookie, iss, os)
        {
            proxy_ = prx;
            _encoding = Protocol.getCompatibleEncoding(proxy_.reference__().getEncoding());
        }

        public void prepare(string operation, Ice.OperationMode mode, Dictionary<string, string> context,
                              bool explicitContext, bool synchronous)
        {
            _handler = null;
            _sent = false;
            _cnt = 0;
            _mode = mode;
            sentSynchronously_ = false;
            synchronous_ = synchronous;

            Protocol.checkSupportedProtocol(Protocol.getCompatibleProtocol(proxy_.reference__().getProtocol()));

            if(explicitContext && context == null)
            {
                context = emptyContext_;
            }

            observer_ = ObserverHelper.get(proxy_, operation, context);

            switch(proxy_.reference__().getMode())
            {
                case Reference.Mode.ModeTwoway:
                case Reference.Mode.ModeOneway:
                case Reference.Mode.ModeDatagram:
                {
                    os_.writeBlob(Protocol.requestHdr);
                    break;
                }

                case Reference.Mode.ModeBatchOneway:
                case Reference.Mode.ModeBatchDatagram:
                {
                    while(true)
                    {
                        try
                        {
                            _handler = proxy_.getRequestHandler__();
                            _handler.prepareBatchRequest(os_);
                            break;
                        }
                        catch(RetryException)
                        {
                            // Clear request handler and retry.
                            proxy_.setRequestHandler__(_handler, null);
                        }
                        catch(Ice.LocalException ex)
                        {
                            if(observer_ != null)
                            {
                                observer_.failed(ex.ice_name());
                            }
                            // Clear request handler
                            proxy_.setRequestHandler__(_handler, null);
                            _handler = null;
                            throw ex;
                        }
                    }
                    break;
                }
            }

            Reference rf = proxy_.reference__();

            rf.getIdentity().write__(os_);

            //
            // For compatibility with the old FacetPath.
            //
            string facet = rf.getFacet();
            if(facet == null || facet.Length == 0)
            {
                os_.writeStringSeq(null);
            }
            else
            {
                string[] facetPath = { facet };
                os_.writeStringSeq(facetPath);
            }

            os_.writeString(operation);

            os_.writeByte((byte)mode);

            if(context != null)
            {
                //
                // Explicit context
                //
                Ice.ContextHelper.write(os_, context);
            }
            else
            {
                //
                // Implicit context
                //
                Ice.ImplicitContextI implicitContext = rf.getInstance().getImplicitContext();
                Dictionary<string, string> prxContext = rf.getContext();

                if(implicitContext == null)
                {
                    Ice.ContextHelper.write(os_, prxContext);
                }
                else
                {
                    implicitContext.write(prxContext, os_);
                }
            }
        }

        public override Ice.ObjectPrx getProxy()
        {
            return proxy_;
        }

        public bool send(Ice.ConnectionI connection, bool compress, bool response, out Ice.AsyncCallback sentCB)
        {
            // Store away the connection for passing to the dispatcher.
            cachedConnection_ = connection;
            return connection.sendAsyncRequest(this, compress, response, out sentCB);
        }

        public bool invokeCollocated(CollocatedRequestHandler handler, out Ice.AsyncCallback sentCallback)
        {
            // The BasicStream cannot be cached if the proxy is
            // not a twoway or there is an invocation timeout set.
            if(!proxy_.ice_isTwoway() || proxy_.reference__().getInvocationTimeout() > 0)
            {
                // Disable caching by marking the streams as cached!
                state_ |= StateCachedBuffers;
            }
            handler.invokeAsyncRequest(this, synchronous_, out sentCallback);
            return false;
        }
        
        public Ice.AsyncCallback sent()
        {
            lock(monitor_)
            {
                bool alreadySent = (state_ & StateSent) != 0;
                state_ |= StateSent;
                _sent = true;
    
                Debug.Assert((state_ & StateDone) == 0);
                if(!proxy_.ice_isTwoway())
                {
                    if(childObserver_ != null)
                    {
                        childObserver_.detach();
                        childObserver_ = null;
                    }
                    if(observer_ != null && sentCallback_ == null)
                    {
                        observer_.detach();
                        observer_ = null;
                    }
                    if(timeoutRequestHandler_ != null)
                    {
                        instance_.timer().cancel(this);
                        timeoutRequestHandler_ = null;
                    }
                    state_ |= StateDone | StateOK;
                    //_os.resize(0, false); // Don't clear the buffer now, it's needed for the collocation optimization
                    if(waitHandle_ != null)
                    {
                        waitHandle_.Set();
                    }
                }
                System.Threading.Monitor.PulseAll(monitor_);

                return alreadySent ? null : sentCallback_; // Don't call the sent call is already sent.
            }
        }

        public new void invokeSent(Ice.AsyncCallback cb)
        {
            base.invokeSent(cb);
        }

        public void finished(Ice.Exception exc)
        {
            lock(monitor_)
            {
                Debug.Assert((state_ & StateDone) == 0);
                if(childObserver_ != null)
                {
                    childObserver_.failed(exc.ice_name());
                    childObserver_.detach();
                    childObserver_ = null;
                }
                if(timeoutRequestHandler_ != null)
                {
                    instance_.timer().cancel(this);
                    timeoutRequestHandler_ = null;
                }
            }

            //
            // NOTE: at this point, synchronization isn't needed, no other threads should be
            // calling on the callback.
            //

            try
            {
                if(!handleException(exc))
                {
                    return; // Can't be retried immediately.
                }
            
                invoke(false); // Retry the invocation
            }
            catch(Ice.Exception ex)
            {
                invokeException(ex);
            }
        }

        public void 
        dispatchInvocationCancel(Ice.LocalException ex, ThreadPool threadPool, Ice.Connection connection)
        {
            OutgoingAsync self = this;
            threadPool.dispatch(() => 
            {
                self.finished(ex);
            }, connection);
        }

        public void finished()
        {
            Debug.Assert(proxy_.ice_isTwoway()); // Can only be called for twoways.

            byte replyStatus;
            Ice.AsyncCallback cb = null;
            try
            {
                lock(monitor_)
                {
                    Debug.Assert(exception_ == null && (state_ & StateDone) == 0);
                    Debug.Assert(is_ != null);

                    if(childObserver_ != null)
                    {
                        childObserver_.reply(is_.size() - Protocol.headerSize - 4);
                        childObserver_.detach();
                        childObserver_ = null;
                    }

                    if(timeoutRequestHandler_ != null)
                    {
                        instance_.timer().cancel(this);
                        timeoutRequestHandler_ = null;
                    }
                    
                    replyStatus = is_.readByte();

                    switch(replyStatus)
                    {
                        case ReplyStatus.replyOK:
                        {
                            break;
                        }

                        case ReplyStatus.replyUserException:
                        {
                            if(observer_ != null)
                            {
                                observer_.userException();
                            }
                            break;
                        }

                        case ReplyStatus.replyObjectNotExist:
                        case ReplyStatus.replyFacetNotExist:
                        case ReplyStatus.replyOperationNotExist:
                        {
                            Ice.Identity id = new Ice.Identity();
                            id.read__(is_);

                            //
                            // For compatibility with the old FacetPath.
                            //
                            string[] facetPath = is_.readStringSeq();
                            string facet;
                            if(facetPath.Length > 0)
                            {
                                if(facetPath.Length > 1)
                                {
                                    throw new Ice.MarshalException();
                                }
                                facet = facetPath[0];
                            }
                            else
                            {
                                facet = "";
                            }

                            string operation = is_.readString();

                            Ice.RequestFailedException ex = null;
                            switch(replyStatus)
                            {
                            case ReplyStatus.replyObjectNotExist:
                            {
                                ex = new Ice.ObjectNotExistException();
                                break;
                            }

                            case ReplyStatus.replyFacetNotExist:
                            {
                                ex = new Ice.FacetNotExistException();
                                break;
                            }

                            case ReplyStatus.replyOperationNotExist:
                            {
                                ex = new Ice.OperationNotExistException();
                                break;
                            }

                            default:
                            {
                                Debug.Assert(false);
                                break;
                            }
                            }

                            ex.id = id;
                            ex.facet = facet;;
                            ex.operation = operation;
                            throw ex;
                        }

                        case ReplyStatus.replyUnknownException:
                        case ReplyStatus.replyUnknownLocalException:
                        case ReplyStatus.replyUnknownUserException:
                        {
                            string unknown = is_.readString();

                            Ice.UnknownException ex = null;
                            switch(replyStatus)
                            {
                            case ReplyStatus.replyUnknownException:
                            {
                                ex = new Ice.UnknownException();
                                break;
                            }
                            case ReplyStatus.replyUnknownLocalException:
                            {
                                ex = new Ice.UnknownLocalException();
                                break;
                            }
                            case ReplyStatus.replyUnknownUserException:
                            {
                                ex = new Ice.UnknownUserException();
                                break;
                            }
                            default:
                            {
                                Debug.Assert(false);
                                break;
                            }
                            }
                            ex.unknown = unknown;
                            throw ex;
                        }

                        default:
                        {
                            throw new Ice.UnknownReplyStatusException();
                        }
                    }

                    state_ |= StateDone;
                    //os_.resize(0, false); // Clear buffer now, instead of waiting for AsyncResult deallocation
                    if(waitHandle_ != null)
                    {
                        waitHandle_.Set();
                    }
                    if(replyStatus == ReplyStatus.replyOK)
                    {
                        state_ |= StateOK;
                    }
                    cb = completedCallback_;
                    System.Threading.Monitor.PulseAll(monitor_);
                }
            }
            catch(Ice.LocalException ex)
            {
                finished(ex);
                return;
            }

            Debug.Assert(replyStatus == ReplyStatus.replyOK || replyStatus == ReplyStatus.replyUserException);
            invokeCompleted(cb);
        }

        public bool invoke(bool synchronous)
        {
            Reference.Mode mode = proxy_.reference__().getMode();
            if(mode == Reference.Mode.ModeBatchOneway || mode == Reference.Mode.ModeBatchDatagram)
            {
                state_ |= StateDone | StateOK;
                _handler.finishBatchRequest(os_);
                if(observer_ != null)
                {
                    observer_.detach();
                    observer_ = null;
                }
                return true;
            }

            while(true)
            {
                try
                {
                    _sent = false;
                    _handler = proxy_.getRequestHandler__();
                    Ice.AsyncCallback sentCallback;
                    bool sent = _handler.sendAsyncRequest(this, out sentCallback);
                    if(sent)
                    {
                        if(synchronous) // Only set sentSynchronously_ If called synchronously by the user thread.
                        {
                            sentSynchronously_ = true;
                            invokeSent(sentCallback);
                        }
                        else
                        {
                            invokeSentAsync(sentCallback);
                        }
                    }

                    if(proxy_.ice_isTwoway() || !sent)
                    {
                        lock(monitor_)
                        {
                            if((state_ & StateDone) == 0)
                            {
                                int invocationTimeout = _handler.getReference().getInvocationTimeout();
                                if(invocationTimeout > 0)
                                {
                                    instance_.timer().schedule(this, invocationTimeout);
                                    timeoutRequestHandler_ = _handler;
                                }
                            }
                        }
                    }
                    break;
                }
                catch(RetryException)
                {

                    proxy_.setRequestHandler__(_handler, null); // Clear request handler and retry.
                }
                catch(Ice.Exception ex)
                {
                    if(!handleException(ex)) // This will throw if the invocation can't be retried.
                    {
                        break; // Can't be retried immediately.
                    }
                }
            }
            return sentSynchronously_;
        }

        public IceInternal.BasicStream startReadParams()
        {
            is_.startReadEncaps();
            return is_;
        }

        public void endReadParams()
        {
            is_.endReadEncaps();
        }

        public void readEmptyParams()
        {
            is_.skipEmptyEncaps();
        }

        public byte[] readParamEncaps()
        {
            return is_.readEncaps(out _encoding);
        }

        public BasicStream startWriteParams(Ice.FormatType format)
        {
            os_.startWriteEncaps(_encoding, format);
            return os_;
        }

        public void endWriteParams()
        {
            os_.endWriteEncaps();
        }

        public void writeEmptyParams()
        {
            os_.writeEmptyEncaps(_encoding);
        }

        public void writeParamEncaps(byte[] encaps)
        {
            if(encaps == null || encaps.Length == 0)
            {
                os_.writeEmptyEncaps(_encoding);
            }
            else
            {
                os_.writeEncaps(encaps);
            }
        }

        public IceInternal.BasicStream istr__
        {
            get
            {
                if(is_ == null) // _is can already be initialized if the invocation is retried
                {
                    is_ = new IceInternal.BasicStream(instance_, Ice.Util.currentProtocolEncoding);
                }
                return is_;
            }
        }

        public void 
        runTimerTask()
        {
            runTimerTask__();
        }

        public void
        cacheMessageBuffers()
        {
            if(proxy_.reference__().getInstance().cacheMessageBuffers() > 0)
            {
                lock(this)
                {
                    if((state_ & StateCachedBuffers) > 0) {
                        return;
                    }
                    state_ |= StateCachedBuffers;
                }

                if(is_ != null)
                {
                    is_.reset();
                }
                os_.reset();
           
                proxy_.cacheMessageBuffers(is_, os_);
            }
        }

        override public void invokeExceptionAsync(Ice.Exception ex)
        {
            if((state_ & StateDone) == 0 && _handler != null)
            {
                //
                // If we didn't finish a batch oneway or datagram request, we
                // must notify the connection about that we give up ownership
                // of the batch stream.
                //
                Reference.Mode mode = proxy_.reference__().getMode();
                if(mode == Reference.Mode.ModeBatchOneway || mode == Reference.Mode.ModeBatchDatagram)
                {
                    _handler.abortBatchRequest();
                }
            }
            base.invokeExceptionAsync(ex);
        }

        private bool handleException(Ice.Exception exc)
        {
            try
            {
                int interval = proxy_.handleException__(exc, _handler, _mode, _sent, ref _cnt);
                if(observer_ != null)
                {
                    observer_.retried(); // Invocation is being retried.
                }
                if(interval > 0)
                {
                    instance_.retryQueue().add(this, interval);
                    return false; // Don't retry immediately, the retry queue will take care of the retry.
                }
                else
                {
                    return true; // Retry immediately.
                }
            }
            catch(Ice.Exception ex)
            {
                if(observer_ != null)
                {
                    observer_.failed(ex.ice_name());
                }
                throw ex;
            }
        }

        private class TaskI : TimerTask
        {
            internal TaskI(OutgoingAsync @out)
            {
                _out = @out;
            }

            public void runTimerTask()
            {
                _out.runTimerTask__();
            }

            private OutgoingAsync _out;
        }

        protected Ice.ObjectPrxHelperBase proxy_;

        private RequestHandler _handler;
        private Ice.EncodingVersion _encoding;
        private int _cnt;
        private Ice.OperationMode _mode;
        private bool _sent;

        private static Dictionary<string, string> emptyContext_ = new Dictionary<string, string>();
    }

    abstract public class OutgoingAsync<T> : OutgoingAsync, Ice.AsyncResult<T>
    {
        public OutgoingAsync(Ice.ObjectPrxHelperBase prx, string operation, object cookie) :
            base(prx, operation, cookie)
        {
        }

        public OutgoingAsync(Ice.ObjectPrxHelperBase prx, string operation, object cookie, BasicStream iss, 
                             BasicStream os) :
            base(prx, operation, cookie, iss, os)
        {
        }

        new public Ice.AsyncResult<T> whenCompleted(Ice.ExceptionCallback excb)
        {
            lock(monitor_)
            {
                if(excb == null)
                {
                    throw new System.ArgumentException("callback is null");
                }
                setCompletedCallback(getCompletedCallback());
                exceptionCallback_ = excb;
                if((state_ & StateDone) == 0)
                {
                    return this;
                }
                else if((getProxy() == null || getProxy().ice_isOneway()) && exception_ == null)
                {
                    return this;
                }
            }


            instance_.clientThreadPool().dispatch(() =>
            {
                try
                {
                    completedCallback_(this);
                }
                catch(System.Exception ex)
                {
                    warning(ex);
                }
            }, null);
            return this;
        }

        virtual public Ice.AsyncResult<T> whenCompleted(T cb, Ice.ExceptionCallback excb)
        {
            lock(monitor_)
            {
                if(cb == null && excb == null)
                {
                    throw new System.ArgumentException("callback is null");
                }
                setCompletedCallback(getCompletedCallback());
                responseCallback_ = cb;
                exceptionCallback_ = excb;
                if((state_ & StateDone) == 0)
                {
                    return this;
                }
                else if((getProxy() == null || getProxy().ice_isOneway()) && exception_ == null)
                {
                    return this;
                }
            }

            instance_.clientThreadPool().dispatch(() =>
            {
                try
                {
                    completedCallback_(this);
                }
                catch(System.Exception ex)
                {
                    warning(ex);
                }
            }, null);

            return this;
        }

        new public Ice.AsyncResult<T> whenSent(Ice.SentCallback cb)
        {
            base.whenSent(cb);
            return this;
        }

        protected T responseCallback_;
    }

    public class TwowayOutgoingAsync<T> : OutgoingAsync<T>
    {
        public TwowayOutgoingAsync(Ice.ObjectPrxHelperBase prx, string operation, ProxyTwowayCallback<T> cb, 
                                   object cookie) :
            base(prx, operation, cookie)
        {
            Debug.Assert(cb != null);
            _completed = cb;
        }

        public TwowayOutgoingAsync(Ice.ObjectPrxHelperBase prx, string operation, ProxyTwowayCallback<T> cb, 
                                   object cookie, BasicStream iss, BasicStream os) :
            base(prx, operation, cookie, iss, os)
        {
            Debug.Assert(cb != null);
            _completed = cb;
        }

        override protected Ice.AsyncCallback getCompletedCallback()
        {
            return completed__;
        }

        private void completed__(Ice.AsyncResult result)
        {
            _completed(this, responseCallback_, exceptionCallback_);
        }

        private ProxyTwowayCallback<T> _completed;
    }

    public class OnewayOutgoingAsync<T> : OutgoingAsync<T>
    {
        public OnewayOutgoingAsync(Ice.ObjectPrxHelperBase prx, string operation, ProxyOnewayCallback<T> cb,
                                   object cookie) :
            base(prx, operation, cookie)
        {
            Debug.Assert(cb != null);
            _completed = cb;
        }

        public OnewayOutgoingAsync(Ice.ObjectPrxHelperBase prx, string operation, ProxyOnewayCallback<T> cb,
                                   object cookie, BasicStream iss, BasicStream os) :
            base(prx, operation, cookie, iss, os)
        {
            Debug.Assert(cb != null);
            _completed = cb;
        }
        override protected Ice.AsyncCallback getCompletedCallback()
        {
            return completed__;
        }

        protected void completed__(Ice.AsyncResult r__)
        {
            try
            {
                IceInternal.OutgoingAsync outAsync__ = (IceInternal.OutgoingAsync)r__;
                ((Ice.ObjectPrxHelperBase)(outAsync__.getProxy())).end__(outAsync__, outAsync__.getOperation());
            }
            catch(Ice.Exception ex__)
            {
                if(exceptionCallback_ != null)
                {
                    exceptionCallback_(ex__);
                }
                return;
            }
            _completed(responseCallback_);
        }

        private ProxyOnewayCallback<T> _completed;
    }

    public class BatchOutgoingAsync : OutgoingAsyncBase, OutgoingAsyncMessageCallback, TimerTask
    {
        public BatchOutgoingAsync(Ice.Communicator communicator, Instance instance, string operation, object cookie) :
            base(communicator, instance, operation, cookie)
        {
        }

        public bool send(Ice.ConnectionI connection, bool compress, bool response, out Ice.AsyncCallback sentCallback)
        {
            // Store away the connection for passing to the dispatcher.
            cachedConnection_ = connection;
            return connection.flushAsyncBatchRequests(this, out sentCallback);
        }
        
        public bool invokeCollocated(CollocatedRequestHandler handler, out Ice.AsyncCallback sentCallback)
        {
            return handler.invokeAsyncBatchRequests(this, out sentCallback);
        }
        
        virtual public Ice.AsyncCallback sent()
        {
            lock(monitor_)
            {
                Debug.Assert((state_ & (StateDone | StateOK | StateSent)) == 0);
                state_ |= (StateDone | StateOK | StateSent);
                //_os.resize(0, false); // Don't clear the buffer now, it's needed for the collocation optimization
                if(childObserver_ != null)
                {
                    childObserver_.detach();
                    childObserver_ = null;
                }
                if(timeoutRequestHandler_ != null)
                {
                    instance_.timer().cancel(this);
                    timeoutRequestHandler_ = null;
                }
                System.Threading.Monitor.PulseAll(monitor_);
                if(waitHandle_ != null)
                {
                    waitHandle_.Set();
                }

                if(sentCallback_ == null)
                {
                    if(observer_ != null)
                    {
                        observer_.detach();
                        observer_ = null;
                    }
                }
                return sentCallback_;
            }
        }

        public new void invokeSent(Ice.AsyncCallback cb)
        {
            base.invokeSent(cb);
        }

        virtual public void finished(Ice.Exception exc)
        {
            lock(this)
            {
                if(childObserver_ != null)
                {
                    childObserver_.failed(exc.ice_name());
                    childObserver_.detach();
                    childObserver_ = null;
                }
                if(timeoutRequestHandler_ != null)
                {
                    instance_.timer().cancel(this);
                    timeoutRequestHandler_ = null;
                }
            }
            invokeException(exc);
        }

        public void 
        dispatchInvocationCancel(Ice.LocalException ex, ThreadPool threadPool, Ice.Connection connection)
        {
            BatchOutgoingAsync self = this;
            threadPool.dispatch(() => 
            {
                self.finished(ex);
            }, connection);
        }

        public void 
        runTimerTask()
        {
            runTimerTask__();
        }
    }

    public class ProxyBatchOutgoingAsync : BatchOutgoingAsync
    {
        public ProxyBatchOutgoingAsync(Ice.ObjectPrxHelperBase proxy, string operation, object cookie) :
            base(proxy.ice_getCommunicator(), proxy.reference__().getInstance(), operation, cookie)
        {
            _proxy = proxy;
            observer_ = ObserverHelper.get(proxy, operation);
        }

        public void invoke()
        {
            Protocol.checkSupportedProtocol(_proxy.reference__().getProtocol());

            RequestHandler handler = null;
            try
            {
                handler = _proxy.getRequestHandler__();
                Ice.AsyncCallback sentCallback;
                if(handler.sendAsyncRequest(this, out sentCallback))
                {
                    sentSynchronously_ = true;
                    if(sentCallback != null)
                    {
                        invokeSent(sentCallback);
                    }
                }
                else
                {
                    lock(monitor_)
                    {
                        if((state_ & StateDone) == 0)
                        {
                            int invocationTimeout = handler.getReference().getInvocationTimeout();
                            if(invocationTimeout > 0)
                            {
                                instance_.timer().schedule(this, invocationTimeout);
                                timeoutRequestHandler_ = handler;
                            }
                        }
                    }
                }
            }
            catch(RetryException)
            {
                //
                // Clear request handler but don't retry or throw. Retrying
                // isn't useful, there were no batch requests associated with
                // the proxy's request handler.
                //
                _proxy.setRequestHandler__(handler, null);
            }
            catch(Ice.Exception ex)
            {
                if(observer_ != null)
                {
                    observer_.failed(ex.ice_name());
                }
                _proxy.setRequestHandler__(handler, null); // Clear request handler
                throw ex; // Throw to notify the user lthat batch requests were potentially lost.
            }
        }

        public override Ice.ObjectPrx getProxy()
        {
            return _proxy;
        }

        private Ice.ObjectPrxHelperBase _proxy;
    }

    public class ConnectionBatchOutgoingAsync : BatchOutgoingAsync
    {
        public ConnectionBatchOutgoingAsync(Ice.ConnectionI con, Ice.Communicator communicator, Instance instance,
                                            string operation, object cookie) :
            base(communicator, instance, operation, cookie)
        {
            _connection = con;
        }

        public void invoke()
        {
            Ice.AsyncCallback sentCallback;
            if(_connection.flushAsyncBatchRequests(this, out sentCallback))
            {
                sentSynchronously_ = true;
                invokeSent(sentCallback);
            }
        }

        public override Ice.Connection getConnection()
        {
            return _connection;
        }

        private Ice.ConnectionI _connection;
    }

    public class CommunicatorBatchOutgoingAsync : OutgoingAsyncBase
    {
        public CommunicatorBatchOutgoingAsync(Ice.Communicator communicator, Instance instance, String operation,
                                              object cookie) :
            base(communicator, instance, operation, cookie)
        {
            //
            // _useCount is initialized to 1 to prevent premature callbacks.
            // The caller must invoke ready() after all flush requests have
            // been initiated.
            //
            _useCount = 1;

            //
            // Assume all connections are flushed synchronously.
            //
            sentSynchronously_ = true;

            //
            // Attach observer
            //
            observer_ = ObserverHelper.get(instance, operation);
        }

        public void flushConnection(Ice.ConnectionI con)
        {
            lock(monitor_)
            {
                ++_useCount;
            }


            try
            {
                Ice.AsyncCallback sentCallback;
                if(!con.flushAsyncBatchRequests(new BatchOutgoingAsyncI(this), out sentCallback))
                {
                    sentSynchronously_ = false;
                }
                Debug.Assert(sentCallback == null);
            }
            catch(Ice.LocalException)
            {
                check(false);
                throw;
            }
        }

        public void ready()
        {
            check(true);
        }

        private void check(bool userThread)
        {
            Ice.AsyncCallback sentCallback = null;
            lock(monitor_)
            {
                Debug.Assert(_useCount > 0);
                if(--_useCount > 0)
                {
                    return;
                }

                state_ |= (StateDone | StateOK | StateSent);
                os_.resize(0, false); // Clear buffer now, instead of waiting for AsyncResult deallocation
                sentCallback = sentCallback_;
                System.Threading.Monitor.PulseAll(monitor_);
                if(waitHandle_ != null)
                {
                    waitHandle_.Set();
                }
            }

            //
            // sentSynchronously_ is immutable here.
            //
            if(sentCallback == null)
            {
                if(observer_ != null)
                {
                    observer_.detach();
                    observer_ = null;
                }
            }
            else
            {
                if(!sentSynchronously_ || !userThread)
                {
                    invokeSentAsync(sentCallback);
                }
                else
                {
                    invokeSent(sentCallback);
                }
            }
        }

        class BatchOutgoingAsyncI : BatchOutgoingAsync
        {
            public BatchOutgoingAsyncI(CommunicatorBatchOutgoingAsync outAsync) :
                base(outAsync.communicator_, outAsync.instance_, outAsync.operation_, null)
            {
                _outAsync = outAsync;
            }

            override public Ice.AsyncCallback sent()
            {
                if(childObserver_ != null)
                {
                    childObserver_.detach();
                    childObserver_ = null;
                }
                _outAsync.check(false);
                return null;
            }

            override public void finished(Ice.Exception ex)
            {
                if(childObserver_ != null)
                {
                    childObserver_.failed(ex.ice_name());
                    childObserver_.detach();
                    childObserver_ = null;
                }
                _outAsync.check(false);
            }

            override public void attachRemoteObserver(Ice.ConnectionInfo info, Ice.Endpoint endpt, 
                                                        int requestId, int sz)
            {
                if(_outAsync.observer_ != null)
                {
                    childObserver_ = _outAsync.observer_.getRemoteObserver(info, endpt, requestId, sz);
                    if(childObserver_ != null)
                    {
                        childObserver_.attach();
                    }
                }
            }

            private CommunicatorBatchOutgoingAsync _outAsync;
        };

        private int _useCount;
    }
}

namespace Ice
{

    /// <summary>
    /// Deprecated callback interface for AMI invocations. This is useful
    /// for applications that send asynchronous invocations that
    /// might not be sent for some time and, therefore, are
    /// queued in the Ice run time for transmission.
    /// The Ice run time calls ice_sent if an
    /// an AMI invocation cannot be written to the transport and was
    /// queued for later transmission. Applications that need to
    /// implement flow control for AMI invocations can implement
    /// this interface on the corresponding AMI callback object.
    /// </summary>
    public interface AMISentCallback
    {
        /// <summary>
        /// Indicates to the caller of an AMI operation that
        /// the invocation was queued for later transmission.
        /// </summary>
        void ice_sent();
    }

    public abstract class AMICallbackBase
    {
        /// <summary>
        /// The Ice run time calls ice_exception when an asynchronous operation invocation
        /// raises an Ice run-time exception.
        /// </summary>
        /// <param name="ex">The encoded Ice run-time exception raised by the operation.</param>
        public abstract void ice_exception(Ice.Exception ex);

        public void exception__(Ice.Exception ex)
        {
            ice_exception(ex);
        }

        public void sent__(Ice.AsyncResult result)
        {
            if(!result.sentSynchronously())
            {
                ((AMISentCallback)this).ice_sent();
            }
        }
    }
}
