// **********************************************************************
//
// Copyright (c) 2001
// MutableRealms, Inc.
// Huntsville, AL, USA
//
// All Rights Reserved
//
// **********************************************************************

#include <Evictor.h>
#include <PhoneBookI.h>
#include <sstream>

using namespace std;
using namespace Ice;
using namespace Freeze;

Evictor::Evictor(const DBPtr& db, Int size) :
    _db(db),
    _evictorSize(static_cast<map<string, EvictorEntry>::size_type>(size))
{
}

ObjectPtr
Evictor::locate(const ObjectAdapterPtr&, const string& identity, ObjectPtr&)
{
    JTCSyncT<JTCMutex> sync(*this);
    
    map<string, EvictorEntry>::iterator p = _evictorMap.find(identity);
    if (p != _evictorMap.end())
    {
	//
	// Ice Object found in evictor map. Push it to the front of
	// the evictor list, so that it will be evicted last.
	//
	assert(*(p->second._position) == identity);
	_evictorList.erase(p->second._position);
	_evictorList.push_front(identity);
	p->second._position = _evictorList.begin();

	//
	// Return the servant for the Ice Object.
	//
	return p->second._servant;
    }
    else
    {
	//
	// With most STL implementations, _evictorMap.size() is faster
	// than _evictorList.size().
	//
	if (_evictorMap.size() > _evictorSize)
	{
	    //
	    // Evictor size exceeded. Remove last element.
	    //
	    string evictedIdentity = _evictorList.back();
	    _evictorList.pop_back();
	    _evictorMap.erase(evictedIdentity);
	    assert(_evictorMap.size() == _evictorSize);
	}

	//
	// Find the Ice Object in the database and create a servant
	// for it.
	//
	ObjectPtr servant = _db->get(identity);
	if (!servant)
	{
	    //
            // Ice object with the given identity does not exist,
            // client will get an ObjectNotExistException.
	    //
	    return 0;
	}

	//
	// TODO: That's the only PhoneBook specific stuff!
	//
	EntryI* entry = dynamic_cast<EntryI*>(servant.get());
	assert(entry);
	entry->setIdentity(identity);

	//
	// Add the new Ice Object with its Servant to the evictor list
	// and evictor map.
	//
	_evictorList.push_front(identity);
	EvictorEntry evictorEntry;
	evictorEntry._servant = servant;
	evictorEntry._position = _evictorList.begin();
	_evictorMap[identity] = evictorEntry;

	//
	// Return the new servant for the Ice Object from the database.
	//
	return servant;
    }
}

void
Evictor::finished(const ObjectAdapterPtr&, const string&, const ObjectPtr&, const ObjectPtr&)
{
}
