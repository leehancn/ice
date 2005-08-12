// **********************************************************************
//
// Copyright (c) 2003-2005 ZeroC, Inc. All rights reserved.
//
// This copy of Ice-E is licensed to you under the terms described in the
// ICEE_LICENSE file included in this distribution.
//
// **********************************************************************

#ifndef ICE_COMMUNICATOR_H
#define ICE_COMMUNICATOR_H

#include <IceE/CommunicatorF.h>
#include <IceE/LoggerF.h>
#include <IceE/PropertiesF.h>
#include <IceE/InstanceF.h>

#include <IceE/RecMutex.h>
#include <IceE/Initialize.h> // For the friend declarations.

namespace Ice
{

class ICE_API Communicator : public ::IceUtil::RecMutex, public ::IceUtil::Shared
{
public:
    
    void destroy();
    void shutdown();
    void waitForShutdown();

    ObjectPrx stringToProxy(const std::string&) const;
    std::string proxyToString(const ObjectPrx&) const;

    ObjectAdapterPtr createObjectAdapter(const std::string&);
    ObjectAdapterPtr createObjectAdapterWithEndpoints(const std::string&, const std::string&);

    void setDefaultContext(const Context&);
    Context getDefaultContext() const;

    PropertiesPtr getProperties() const;

    LoggerPtr getLogger() const;
    void setLogger(const LoggerPtr&);

#ifdef ICE_HAS_ROUTER
    RouterPrx getDefaultRouter() const;
    void setDefaultRouter(const RouterPrx&);
#endif

#ifdef ICE_HAS_LOCATOR
    LocatorPrx getDefaultLocator() const;
    void setDefaultLocator(const LocatorPrx&);
#endif

#ifdef ICE_HAS_BATCH
    void flushBatchRequests();
#endif

private:

    Communicator(const PropertiesPtr&);
    ~Communicator();

    //
    // Certain initialization tasks need to be completed after the
    // constructor.
    //
    void finishSetup(int&, char*[]);

    friend ICE_API CommunicatorPtr initialize(int&, char*[], Int);
    friend ICE_API CommunicatorPtr initializeWithProperties(int&, char*[], const PropertiesPtr&, Int);
    friend ICE_API ::IceInternal::InstancePtr IceInternal::getInstance(const ::Ice::CommunicatorPtr&);

    bool _destroyed;
    ::IceInternal::InstancePtr _instance;
    ::Ice::Context _dfltContext;
};

}

#endif
