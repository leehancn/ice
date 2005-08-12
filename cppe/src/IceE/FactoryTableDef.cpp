// **********************************************************************
//
// Copyright (c) 2003-2005 ZeroC, Inc. All rights reserved.
//
// This copy of Ice-E is licensed to you under the terms described in the
// ICEE_LICENSE file included in this distribution.
//
// **********************************************************************

#include <IceE/FactoryTableDef.h>
#include <IceE/UserExceptionFactory.h>

#ifdef __APPLE__
#   include <dlfcn.h>
#endif

namespace IceInternal
{

FactoryTableWrapper factoryTableWrapper;	// Single global instance of the wrapper object that
						// initializes factoryTable.

ICE_API FactoryTableDef* factoryTable;		// Single global instance of the factory table for
						// non-local exceptions and non-abstract classes
}

//
// Add a factory to the exception factory table.
// If the factory is present already, increment its reference count.
//
void
IceInternal::FactoryTableDef::addExceptionFactory(const std::string& t, const IceInternal::UserExceptionFactoryPtr& f)
{
    IceUtil::Mutex::Lock lock(_m);
    EFTable::iterator i = _eft.find(t);
    if(i == _eft.end())
    {
	_eft[t] = EFPair(f, 1);
    }
    else
    {
	i->second.second++;
    }
}

//
// Return the exception factory for a given type ID
//
IceInternal::UserExceptionFactoryPtr
IceInternal::FactoryTableDef::getExceptionFactory(const std::string& t) const
{
    IceUtil::Mutex::Lock lock(_m);
    EFTable::const_iterator i = _eft.find(t);
#ifdef __APPLE__
    if(i == _eft.end())
    {
	lock.release();

	//
	// Try to find the symbol, if found this should trigger the
	// object static constructors to be called.
	//
	std::string symbol = "__F";
	for(std::string::const_iterator p = t.begin(); p != t.end(); ++p)
	{
	    symbol += ((*p) == ':') ? '_' : *p;
	}
	symbol += "__initializer";
	dlsym(RTLD_DEFAULT, symbol.c_str());

	lock.acquire();	

	i = _eft.find(t);
    }
#endif
    return i != _eft.end() ? i->second.first : IceInternal::UserExceptionFactoryPtr();
}

//
// Remove a factory from the exception factory table. If the factory
// is not present, do nothing; otherwise, decrement the factory's
// reference count; if the count drops to zero, remove the factory's
// entry from the table.
//
void
IceInternal::FactoryTableDef::removeExceptionFactory(const std::string& t)
{
    IceUtil::Mutex::Lock lock(_m);
    EFTable::iterator i = _eft.find(t);
    if(i != _eft.end())
    {
	if(--i->second.second == 0)
	{
	    _eft.erase(i);
	}
    }
}

//
// The code generated by slice2cpp contains a file static instance of FactoryTable.
// The constructor of FactoryTable calls initialize(), as does the constructor of
// FactoryTableWrapper. This ensures that the global factoryTable variable is initialized
// before it can be used, regardless of the order of initialization of global objects.
//
IceInternal::FactoryTableWrapper::FactoryTableWrapper()
{
    initialize();
}

IceInternal::FactoryTableWrapper::~FactoryTableWrapper()
{
    finalize();
}

//
// Initialize the single global instance of the factory table, counting
// the number of calls made.
//
void
IceInternal::FactoryTableWrapper::initialize()
{
    IceUtil::StaticMutex::Lock lock(_m);
    if(_initCount == 0)
    {
	factoryTable = new FactoryTableDef;
    }
    ++_initCount;
}

//
// Delete the table if its reference count drops to zero.
//
void
IceInternal::FactoryTableWrapper::finalize()
{
    IceUtil::StaticMutex::Lock lock(_m);
    if(--_initCount == 0)
    {
	delete factoryTable;
    }
}

IceUtil::StaticMutex IceInternal::FactoryTableWrapper::_m = ICE_STATIC_MUTEX_INITIALIZER;
int IceInternal::FactoryTableWrapper::_initCount = 0;	// Initialization count
