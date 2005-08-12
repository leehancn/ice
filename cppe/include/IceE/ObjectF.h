// **********************************************************************
//
// Copyright (c) 2003-2005 ZeroC, Inc. All rights reserved.
//
// This copy of Ice-E is licensed to you under the terms described in the
// ICEE_LICENSE file included in this distribution.
//
// **********************************************************************

#ifndef ICE_OBJECT_F_H
#define ICE_OBJECT_F_H

#include <IceE/Handle.h>

namespace Ice
{

class Object;

}

namespace IceInternal
{

ICE_API void incRef(::Ice::Object*);
ICE_API void decRef(::Ice::Object*);

}

namespace Ice
{

typedef IceInternal::Handle< Object > ObjectPtr;

}

#endif
