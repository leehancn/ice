#!/usr/bin/env python
# **********************************************************************
#
# Copyright (c) 2003-2005 ZeroC, Inc. All rights reserved.
#
# This copy of Ice is licensed to you under the terms described in the
# ICE_LICENSE file included in this distribution.
#
# **********************************************************************
import os, sys, shutil, re, string, getopt, glob

#
# TODO:
#
#  * Finalize what is going where.
#  * Tidying and tracing.
#  * Testing on platforms besides Linux.
#  * Ant build tasks for Ice got missed.
#  * Demo build system
#
# NOTES:
#  There are python-ese ways to do some of the things I've shelled out to do.
#  We might want to convert some of these things, but a lot of things can be
#  done with one command.
#

#
# defaults.
#
verbose = False

#
# fileLists is a mapping of all of the files that will go into a binary distribution.
#
fileLists = dict()

class Package:
    """Encapsulates RPM spec file information to be used to generate a spec file on Linux
       and create RPMs for Ice"""
    def __init__(self, name, requires, summary, group, description, filelist):
        self.name = name
        self.requires = requires
        self.summary = summary
        self.group = group
        self.description = description
        self.filelist = filelist
        
    def writeHdr(self, ofile):
        ofile.write("\n")

    def writeFileList(self, ofile, version, intVersion):
        ofile.write("%defattr(644, root, root, 755)\n\n")
        for perm, f in self.filelist:
            prefix = ""
            if perm == "exe" or perm == "lib":
                prefix = "%attr(755, root, root) "
            if f.find("%version%"):
                f = f.replace("%version%", version)
            if f.endswith(".so"):
                ofile.write(prefix + "/usr/" + f + "." + version + "\n")
                ofile.write(prefix + "/usr/" + f + "." + str(intVersion) + "\n")
            ofile.write(prefix + "/usr/" + f + "\n")
        ofile.write("\n")    

    def writeFiles(self, ofile, version, intVersion):
        ofile.write("%files\n")
        self.writeFileList(ofile, version, intVersion)


class Subpackage(Package):
    def writeHdr(self, ofile):
        ofile.write("%package " + self.name + "\n")
        ofile.write("Summary: " + self.summary + "\n")
        ofile.write("Group: " + self.group + "\n")
        if not self.requires == "":
            ofile.write("Requires: " + self.requires + "\n")
        ofile.write("%description " + self.name + "\n")
        ofile.write(self.description)

    def writeFiles(self, ofile, version, intVersion):
        ofile.write("%files " + self.name + "\n")
        self.writeFileList(ofile, version, intVersion)

transforms = [ ("slice", "share/slice"),
               ("lib/Ice.jar", "lib/Ice-%version%/Ice.jar" ),
               ("doc", "share/doc/Ice-%version%"),
               ("ICE_LICENSE", "share/doc/Ice-%version%/ICE_LICENSE"),
               ("LICENSE", "share/doc/Ice-%version%/LICENSE"),
               ]
               
fileLists = [
    Package("main",
            "",
            "The Ice runtime and tools.",
            "Development/Libraries Development/Tools System Environment/Libraries",
            "",
            [("doc", "share/doc/Ice-%version%/ICE_LICENSE"),
             ("doc", "share/doc/Ice-%version%/LICENSE"),
             ("exe", "bin/dumpdb"),
             ("exe", "bin/transformdb"),
             ("exe", "bin/glacier2router"),
             ("exe", "bin/icebox"),
             ("exe", "bin/iceboxadmin"),
             ("exe", "bin/icepackadmin"),
             ("exe", "bin/icecpp"),
             ("exe", "bin/icepacknode"),
             ("exe", "bin/icepackregistry"),
             ("exe", "bin/icepatch2calc"),
             ("exe", "bin/icepatch2client"),
             ("exe", "bin/icepatch2server"),
             ("exe", "bin/icestormadmin"),
             ("exe", "bin/slice2docbook"), 
             ("lib", "lib/libFreeze.so"),
             ("lib", "lib/libGlacier2.so"),
             ("lib", "lib/libIceBox.so"),
             ("lib", "lib/libIcePack.so"),
             ("lib", "lib/libIcePatch2.so"),
             ("lib", "lib/libIce.so"),
             ("lib", "lib/libIceSSL.so"),
             ("lib", "lib/libIceStormService.so"),
             ("lib", "lib/libIceStorm.so"),
             ("lib", "lib/libIceUtil.so"),
             ("lib", "lib/libIceXML.so"),
             ("lib", "lib/libSlice.so"),
             ("dir", "share/slice"),
             ("dir", "share/doc/Ice-%version%")]),
    Subpackage("c++-devel",
               "",
               "Ice tools, files and libraries for developing Ice applications in C++",
               "Development/Libraries Development/Tools",
               "",
               [("exe", "bin/slice2cpp"),
                ("exe", "bin/slice2freeze"),
                ("dir", "include")]),
    Subpackage("dotnet",
               "ice mono-core",
               "Ice runtime for C\# applications",
               "Development/Libraries Development/Tools",
               "",
               [("lib", "lib/glacier2cs.dll"), ("lib", "lib/icecs.dll"), ("lib", "lib/icepackcs.dll")]),
    Subpackage("dotnet-devel",
               "ice-dotnet mono-devel",
               "Ice tools for developing Ice applications in C\#",
               "Development/Libraries Development/Tools",
               "",
               [("exe", "bin/slice2cs")]),
    Subpackage("java",
               "",
               "Ice runtime for Java applications",
               "Development/Libraries",
               "",
               [("doc", "lib/Ice-%version%/Ice.jar")]),
    Subpackage("java-devel",
               "ice-java",
               "Ice tools developing Ice applications in Java",
               "Development/Libraries Development/Tools",
               "",
               [("exe", "bin/slice2java"),
                ("exe", "bin/slice2freezej")]),
    Subpackage("python",
               "",
               "Ice runtime for Python applications",
               "Development/Libraries",
               "",
               [("lib", "lib/IcePy.so"), ("dir", "python")]),
    Subpackage("python-devel",
               "ice-python",
               "Ice tools for developing Ice applications in Python",
               "Development/Libraries Development/Tools",
               "",
               [("exe", "bin/slice2py")])
    ]

def getIceVersion(file):
    """Extract the ICE version string from a file."""
    config = open(file, "r")
    return  re.search("ICE_STRING_VERSION \"([0-9\.]*)\"", config.read()).group(1)

def getIceSoVersion(file):
    """Extract the ICE version ordinal from a file."""
    config = open(file, "r")
    intVersion = int(re.search("ICE_INT_VERSION ([0-9]*)", config.read()).group(1))
    majorVersion = intVersion / 10000
    minorVersion = intVersion / 100 - 100 * majorVersion
    return '%d' % (majorVersion * 10 + minorVersion)

def getPlatform():
    """Determine the platform we are building and targetting for"""
    if sys.platform.startswith("win") or sys.platform.startswith("cygwin"):
        return "win32"
    elif sys.platform.startswith("linux"):
        return "linux"
    elif sys.platform.startswith("sunos"):
        return "solaris"
    elif sys.platform.startswith("hp"):
        return "hpux"
    elif sys.platform.startswith("darwin"):
        return "macosx"
    elif sys.platform.startswith("aix"):
        return "aix"
    else:
        return None

def trace(s):
    if verbose:
        print s
    
def initDirectory(d):
    """Check for the existance of the directory. If it isn't there make it."""
    
    if os.path.exists(d):
        #
        # Make sure its a directory and has correct permissions.
        #
        if not os.path.isdir(d):
            print "Path " + d + " exists but is not a directory."
            sys.exit(1)
            
        if os.access(d, os.X_OK | os.R_OK | os.W_OK):
            trace("Path " + d + " exists and is ok, continuing")
        else:
            print "Directory " + d + " exists, but has incorrect permissions"
            sys.exit(1)
    else:
        #
        # This could fail due to lack of permissions.
        #
        os.makedirs(d, 0770)

def getVersion(cvsTag, buildDir):
    """Extracts a source file from the repository and gets the version number from it"""
    cwd = os.getcwd()
    os.chdir(buildDir)
    if getPlatform() == "aix":
        os.environ["LIBPATH"] = ""
        os.system("cvs -d cvsint.mutablerealms.com:/home/cvsroot export -r " + cvsTag + " ice/include/IceUtil/Config.h")
    else:
        os.system("cvs -d cvs.mutablerealms.com:/home/cvsroot export -r " + cvsTag + " ice/include/IceUtil/Config.h")

    result = [ getIceVersion("ice/include/IceUtil/Config.h"), getIceSoVersion("ice/include/IceUtil/Config.h")]
    os.remove("ice/include/IceUtil/Config.h")
    os.removedirs("ice/include/IceUtil")
    os.chdir(cwd)
    return result

def collectSourceDistributions(tag, sourceDir, cvsdir, distro):
    """The location for the source distributions was not supplied so
       we are going to assume we are being called from a CVS tree and we
       are going to go get them ourselves"""
    cwd = os.getcwd()
    os.chdir(cwd + "/../" + cvsdir)
    if cvsdir == "icepy":
        os.system("./makedist.py " + tag)
    else:
        os.system("./makedist.py " + tag)
    shutil.copy("dist/" + distro + ".tar.gz", sourceDir)
    os.chdir(cwd)

def extractDemos(buildDir, version, distro, demoDir):
    """Pulls the demo directory out of a distribution and massages its
       build system so it can be built against an installed version of
       Ice"""
    cwd = os.getcwd()
    os.chdir(buildDir + "/demotree")
    os.system("tar xvfz ../sources/" + distro + ".tar.gz " + distro + "/demo " + distro + "/config " + distro + "/ant")
    shutil.move(distro + "/demo", buildDir + "/Ice-" + version + "-demos/demo_" + demoDir)
    shutil.move(distro + "/config", buildDir + "/Ice-" + version + "-demos/config_" + demoDir)
    if demoDir == "java":
        shutil.move(distro + "/ant", buildDir + "/Ice-" + version + "-demos/ant")

    # Change make includes to point to the right config directories.
    script = "find " + buildDir + "/Ice-" + version + "-demos/demo_" + demoDir + " -name Makefile | xargs "
    script = script + "perl -pi -e 's/config\/Make.rules/config_" + demoDir + "\/Make.rules/'"
    os.system(script)

    # Change make includes within the config directory to point to the right directory.
    script = "find " + buildDir + "/Ice-" + version + "-demos/config_" + demoDir + " -name Make* | xargs "
    script = script + "perl -pi -e 's/config\//config_" + demoDir + "\//'"
    os.system(script)

    # C++ specific build modifications.
    if demoDir == "cpp":
        tcwd = os.getcwd()
        os.chdir(buildDir + "/Ice-" + version + "-demos/config_" + demoDir)
        script = "perl -pi -e 's/^prefix.*$/ifeq (\$(ICE_HOME),)\n   ICE_DIR  \= \/usr\nelse\n"
        script = script + "   ICE_DIR \= \$(ICE_HOME)\n"
        script = script + "endif\n/' Make.rules"
        os.system(script)

        script = "perl -pi -e 's/^([a-z]*dir.*=)\s*\$\(top_srcdir\)\/([A-Za-z]*)$/$1 \\x24\(ICE_DIR\)\/$2/' Make.rules"
        os.system(script)

        script = "perl -pi -e 's/^slicedir.*$/ifeq (\$(ICE_DIR),\/usr)\n    slicedir \= \$(ICE_DIR)\/share\/slice\n"
        script = script + "else\n    slicedir \= \$(ICE_DIR)\/slice\nendif\n/' Make.rules"
        os.system(script)
        
        # Dependency files are all going to be bogus.  The makedepend
        # script doesn't seem to work properly for the slice files.
        os.chdir("..")
        os.system("sh -c 'for f in `find . -name .depend` ; do echo \"\" > $f ; done'")
        
        os.chdir(tcwd)
        
    shutil.rmtree(buildDir + "/demotree/" + distro, True)
    os.chdir(cwd)

def archiveDemoTree(buildDir, version):
    cwd = os.getcwd()
    os.chdir(buildDir)
    os.system("perl -pi -e 's/^prefix.*$/prefix = /opt/Ice-" + version + " Ice-" + version + "-demos/config/Make.rules")
    os.system("tar cvfz Ice-" + version + "-demos.tar.gz Ice-" + version + "-demos")
    os.chdir(cwd)

def makeInstall(buildDir, installDir, distro, clean):
    """Make the distro in buildDir sources and install it to installDir."""
    cwd = os.getcwd()
    os.chdir(buildDir)
    if clean:
        shutil.rmtree(distro, True)
        
    if not os.path.exists(distro):
        os.system("tar xfvz sources/" + distro + ".tar.gz")
        
    os.chdir(distro)

    if distro.startswith("IceJ"):
        shutil.copy( buildDir + "/" + distro + "/lib/Ice.jar", installDir + "/lib")
        os.chdir(cwd)
        return

    os.system("perl -pi -e 's/^prefix.*$/prefix = \$\(RPM_BUILD_ROOT\)/' config/Make.rules")

    if distro.startswith("IcePy"):
        try:
            pyHome = os.environ["PYTHON_HOME"]
        except KeyError:
            pyHome = None
            
        if pyHome == None or pyHome == "":
            trace("PYTHON_HOME is not set, figuring it out and trying that")
            pyHome = sys.exec_prefix
            
        os.system("perl -pi -e 's/^PYTHON.HOME.*$/PYTHON\_HOME \?= "+ pyHome.replace("/", "\/") + "/' config/Make.rules")
    os.system("gmake RPM_BUILD_ROOT=" + installDir + " install")
    os.chdir(cwd)
    
def shlibExtensions(versionString, versionInt):
    """Returns a tuple containing the extensions for the shared library, and the 2 symbolic links (respectively)"""
    platform = getPlatform()
    if platform == "hpux":
        return [".sl." + versionString, ".sl." + versionInt, ".sl"]
    elif platform == "macosx":
        return ["." + versionString + ".dylib", "." + versionInt + ".dylib", ".dylib"]
    else:
        return [".so." + versionString, ".so." + versionInt, ".so"]

def strip(files):
    stripCmd = "strip "
    if getPlatform() == "macosx":
        stripCmd = stripCmd + "-x "
    for f in files:
        print "Stripping " + f
        os.system(stripCmd + f)

def printRPMHeader(ofile, version, release, installDir):
    """Used on Linux only.  Prints out the header portion of an RPM spec file for building the RPM"""
    ofile.write("%define _unpackaged_files_terminate_build 0\n")
    ofile.write("Summary: The Internet Communications Engine (ICE) is a modern alternative to object middleware such ")
    ofile.write("as CORBA\n")
    ofile.write("Name: ice\n")
    ofile.write("Version: " + version + "\n")
    ofile.write("Release: " + release + "\n")
    ofile.write("License: GPL\n")
    ofile.write("""Group: Development/Libraries
Vendor: ZeroC Inc
URL: http://www.zeroc.com/index.html
Source0: http://www.zeroc.com/downloads/%{name}-%{version}.tar.gz
Source1: http://www.zeroc.com/downloads/%{name}J-%{version}.tar.gz
Source2: http://www.zeroc.com/downloads/%{name}Py-%{version}.tar.gz
Source3: http://www.zeroc.com/downloads/%{name}CS-%{version}.tar.gz
""")
    ofile.write("BuildRoot: " + installDir + "\n")
    ofile.write("Prefix: /usr\n")
    ofile.write("""
%description

%prep

%build

%install

%clean


""")

def missingPathParts(source, dest):
    print "Calculating :  " + source + " and " + dest
        
    startPath = dest.find(source)

    result = dest[0:startPath]
    #
    # There is no common element, we'll need to create the whole destination tree.
    #
    if startPath == -1:
        result = dest
    #
    # The common element is at the head, but we already know the path doesn't exists
    # so we need to remaining path elements
    #
    elif startPath == 0:
        result = dest
    #
    # If the common element is not at the tail of destination, then we probably
    # need to create the whole path
    #
    elif startPath + len(source) + 1 < len(dest):
        result = dest
        
    print "Making " + result
    return result

def transformDirectories(transforms, version, installDir):
    """Transforms a directory tree that was created with 'make installs' to an RPM friendly
       directory tree"""
    cwd = os.getcwd()
    os.chdir(installDir + "/Ice-" + version)
    for source, dest in transforms:
        if dest.find("%version%"):
            dest = dest.replace("%version%", version)

        sourcedir = source
        destdir = dest
        if not os.path.isdir(sourcedir):
            sourcedir = os.path.dirname(sourcedir)

        # This is a special problem.  What this implies is that we are trying to move the contents of a directory
        # into a subdirectory of itself.  The regular shutil.move() won't cut it.
        if os.path.isdir(sourcedir) and sourcedir.split("/")[0] == destdir.split("/")[0]:
            shutil.move(source, "./tmp")
            os.makedirs(destdir)
            shutil.move("./tmp", destdir)
        else:
            if not os.path.exists(os.path.dirname(dest)):
                os.makedirs(missingPathParts(sourcedir, destdir))
            shutil.move(source, dest)
    os.chdir(cwd)

def usage():
    """Print usage/help information"""
    print "Usage: " + sys.argv[0] + " [options] [tag]"
    print
    print "Options:"
    print "-h    Show this message."
    print "--build-dir     Specify the directory where the distributions will be unpacked and built."
    print "--install-dir   Specify the directory where the distribution contents will be installed to."
    print "--install-root  Specify the root directory that will appear in the tarball."
    print "--sources       Specify the location of the sources directory.  If this is omitted makebindist"
    print "                will traverse ../icej ../icepy ../icecs, etc and make the distributions for you."
    print "-v, --verbose   Print verbose processing messages."
    print "-t, --tag       Specify the CVS version tag for the packages."
    print "--noclean       Do not clean up current sources where applicable (some bits will still be cleaned."
    print "--nobuild       Run through the process but don't build anything new."
    print "--specfile      Just print the RPM spec file and exit."
    print
    print "If no tag is specified, HEAD is used."

def main():

    #
    # Process args.
    #
    try:
        buildDir = None
        installDir = None
        sources = None
        installRoot = None
        verbose = False
        cvsTag = "HEAD"
        clean = True
        build = True
        version = None
        soVersion = 0
        printSpecFile = False
        
        optionList, args = getopt.getopt(sys.argv[1:], "hvt:",
                                         [ "build-dir=", "install-dir=", "install-root=", "sources=",
                                           "verbose", "tag=", "noclean", "nobuild", "specfile"])
               
    except getopt.GetoptError:
        usage()
        sys.exit(2)

    for o, a in optionList:
        if o == "--build-dir":
            buildDir = a
        elif o == "--install-dir":
            installDir = a
        elif o == "--install-root":
            installRoot = a
        elif o == "--sources":
            sources = a
        elif o in ("-h", "--help"):
            usage()
            sys.exit()
        elif o in ("-v", "--verbose"):
            verbose = True
        elif o in ("-t", "--tag"):
            cvsTag = a
        elif o == "--noclean":
            clean = False
        elif o == "--nobuild":
            build = False
        elif o == "--specfile":
            printSpecFile = True

    if buildDir == None:
        trace("No build directory specified, defaulting to $HOME/tmp/icebuild")
        buildDir = os.environ.get('HOME') + "/tmp/icebuild"

    if installDir == None:
        trace("No install directory specified, default to $HOME/tmp/iceinstall")
        installDir = os.environ.get('HOME') + "/tmp/iceinstall"

    if build:
        #
        # We need to clean the directory out to keep obsolete files from being installed.
        #
        if os.path.exists(installDir):
            shutil.rmtree(installDir, True)

    directories = [buildDir, buildDir + "/sources", buildDir + "/demotree",  installDir]
    for d in directories:
        initDirectory(d)

    version, soVersion = getVersion(cvsTag, buildDir)

    if verbose:
        print "Building binary distributions for Ice-" + version + " on " + getPlatform()
        print "Using build directory: " + buildDir
        print "Using install directory: " + installDir
        if getPlatform() == "linux":
            print "(RPMs will be built)"
        print

    #
    # Primarily for debugging spec file creation.
    #
    if printSpecFile:
        ofile = sys.stdout
        printRPMHeader(ofile, version, "1", installDir)
        for v in fileLists:
            v.writeHdr(ofile)
            ofile.write("\n\n\n")
        for v in fileLists:
            v.writeFiles(ofile, version, soVersion)
            ofile.write("\n")
        sys.exit(0)

    #
    # This last directory we have to wait until we've got the version number for the distribution.
    #
    shutil.rmtree(buildDir + "/Ice-" + version + "-demos", True)
    initDirectory(buildDir + "/Ice-" + version + "-demos/config")

    if build:
        collectSources = False
        if sources == None and clean:
            sources = buildDir + "/sources"
            collectSources = True

        #
        # Ice must be first or building the other source distributions will fail.
        #
        sourceTarBalls = [ ("ice", "Ice-" + version, "cpp"),
                           ("icej","IceJ-" + version, "java"),
                           ("icecs","IceCS-" + version, "cs"),
                           ("icepy","IcePy-" + version, "py") ]

        os.environ['ICE_HOME'] = installDir + "/Ice-" + version
        currentLibraryPath = None
        try:
            currentLibraryPath = os.environ['LD_LIBRARY_PATH'] 
        except KeyError:
            currentLibraryPath = ""

        os.environ['LD_LIBRARY_PATH'] = installDir + "/Ice-" + version + "/lib:" + currentLibraryPath
        os.environ['PATH'] = installDir + "/Ice-" + version + "/bin:" + os.environ['PATH']

        for cvs, tarball, demoDir in sourceTarBalls:
            if collectSources:
                collectSourceDistributions(cvsTag, sources, cvs, tarball)

            extractDemos(buildDir, version, tarball, demoDir)
            makeInstall(buildDir, installDir + "/Ice-" + version, tarball, clean)

        #
        # Pack up demos
        #
        archiveDemoTree(buildDir, version)
        shutil.move(buildDir + "/Ice-" + version + "-demos.tar.gz", installDir + "/Ice-" + version + "-demos.tar.gz")

    #
    # Sources should have already been built and installed.  We
    # can pick the binaries up from the iceinstall directory.
    #
    binaries = glob.glob(installDir + "/Ice-" + version + "/bin/*")
    binaries.extend(glob.glob(installDir + "/Ice-" + version + "/lib/*" + shlibExtensions(version, soVersion)[0]))
    strip(binaries)
    cwd = os.getcwd()
    os.chdir(installDir)
    os.system("tar cvf Ice-" + version + "-bin-" + getPlatform() + ".tar Ice-" + version)
    os.system("gzip -9 Ice-" + version + "-bin-" + getPlatform() + ".tar")
    os.system("zip -9ry Ice-" + version + "-bin-" + getPlatform() + ".zip Ice-" + version)
    os.chdir(cwd)

    #
    # If we are running on Linux, we need to create RPMs.  This will probably blow up unless the user
    # that is running the script has massaged the permissions on /usr/src/redhat/.
    #
    if getPlatform() == "linux":
        transformDirectories(transforms, version, installDir)
        ofile = open(buildDir + "/Ice-" + version + ".spec", "w")
        printRPMHeader(ofile, version, "1", installDir)
        for v in fileLists:
            v.writeHdr(ofile)
            ofile.write("\n\n\n")
        for v in fileLists:
            v.writeFiles(ofile, version, soVersion)
            ofile.write("\n")
        shutil.move(installDir + "/Ice-" + version, installDir + "/usr")
        cwd = os.getcwd()
        os.chdir(buildDir)
        ofile.flush()
        ofile.close()
        os.system("rpmbuild -bb Ice-" + version + ".spec")

    #
    # TODO: Cleanups?  I've left everything in place so that the process can be easily debugged.
    #

if __name__ == "__main__":
    main()
