// **********************************************************************
//
// Copyright (c) 2001
// MutableRealms, Inc.
// Huntsville, AL, USA
//
// All Rights Reserved
//
// **********************************************************************

#include <Ice/Ice.h>
#include <ServantFactory.h>
#include <Pickle.h>

using namespace std;

Ice::ObjectPtr
ServantFactory::create(const string& type)
{
    if (type == "::Persistent")
    {
	return new Persistent;
    }

    assert(false);
    return 0;
}
