// **********************************************************************
//
// Copyright (c) 2003-2005 ZeroC, Inc. All rights reserved.
//
// This copy of Ice-E is licensed to you under the terms described in the
// ICEE_LICENSE file included in this distribution.
//
// **********************************************************************

#ifndef ICE_SERVANT_MANAGER_F_H
#define ICE_SERVANT_MANAGER_F_H

#include <IceE/Handle.h>

namespace IceInternal
{

class ServantManager;
void incRef(ServantManager*);
void decRef(ServantManager*);
typedef Handle<ServantManager> ServantManagerPtr;

}

#endif
