// **********************************************************************
//
// Copyright (c) 2001
// MutableRealms, Inc.
// Huntsville, AL, USA
//
// All Rights Reserved
//
// **********************************************************************

#ifndef PHONE_BOOK_I_H
#define PHONE_BOOK_I_H

#include <Ice/Ice.h>
#include <IceUtil/IceUtil.h>
#include <Freeze/Freeze.h>
#include <PhoneBook.h>

class PhoneBookI;
typedef IceUtil::Handle<PhoneBookI> PhoneBookIPtr;

class EntryI : public Entry, public JTCMutex
{
public:

    EntryI(const PhoneBookIPtr&);

    void setIdentity(const std::string&);

    virtual std::string getName();
    virtual void setName(const std::string&);

    virtual std::string getAddress();
    virtual void setAddress(const std::string&);

    virtual std::string getPhone();
    virtual void setPhone(const std::string&);

    virtual void destroy();

private:

    std::string _identity;
    PhoneBookIPtr _phoneBook;
};

class PhoneBookI : public PhoneBook, public JTCRecursiveMutex
{
public: 

    PhoneBookI(const Ice::ObjectAdapterPtr&);

    virtual EntryPrx createEntry();
    virtual Entries findEntries(const std::string&);
    virtual Names getAllNames();
    
    void add(const std::string&, const std::string&);
    void remove(const std::string&, const std::string&);
    void move(const std::string&, const std::string&, const std::string&);

private:

    Ice::ObjectAdapterPtr _adapter;
    Ice::Long _nextEntryIdentity;
};

#endif
