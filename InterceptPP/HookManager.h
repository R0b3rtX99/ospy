//
// Copyright (c) 2007 Ole Andr� Vadla Ravn�s <oleavr@gmail.com>
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
//

#pragma once

#include "Core.h"
#include "Marshallers.h"
#include "VTable.h"
#include "DLL.h"
#import <msxml6.dll>

namespace InterceptPP {

class StructureFieldDef;

class HookManager : public BaseObject {
public:
	HookManager();
    ~HookManager();

    static HookManager *Instance();

    void LoadDefinitions(const OString &path);

    unsigned int GetFunctionSpecCount() const { return static_cast<unsigned int>(m_funcSpecs.size()); }
    unsigned int GetVTableSpecCount() const { return static_cast<unsigned int>(m_vtableSpecs.size()); }
    unsigned int GetDllModuleCount() const { return static_cast<unsigned int>(m_dllModules.size()); }
    unsigned int GetDllFunctionCount() const { return static_cast<unsigned int>(m_dllFunctions.size()); }
    unsigned int GetVTableCount() const { return static_cast<unsigned int>(m_vtables.size()); }

protected:
    typedef OMap<OString, FunctionSpec *>::Type FunctionSpecMap;
    typedef OMap<OString, VTableSpec *>::Type VTableSpecMap;
    typedef OMap<OICString, DllModule *>::Type DllModuleMap;
    typedef OList<DllFunction *>::Type DllFunctionList;
    typedef OList<VTable *>::Type VTableList;

    FunctionSpecMap m_funcSpecs;
    VTableSpecMap m_vtableSpecs;
    DllModuleMap m_dllModules;
    DllFunctionList m_dllFunctions;
    VTableList m_vtables;

    void ParseTypeNode(MSXML2::IXMLDOMNodePtr &typeNode);
    void ParseStructureNode(MSXML2::IXMLDOMNodePtr &structNode);
    StructureFieldDef *ParseStructureFieldNode(MSXML2::IXMLDOMNodePtr &fieldNode);
    void ParseEnumerationNode(MSXML2::IXMLDOMNodePtr &enumNode);
    bool ParseEnumerationMemberNode(MSXML2::IXMLDOMNodePtr &enumMemberNode, OString &memberName, DWORD &memberValue);

    FunctionSpec *ParseFunctionSpecNode(MSXML2::IXMLDOMNodePtr &funcSpecNode, OString &id, bool nameRequired=true, bool ignoreUnknown=false);
    ArgumentSpec *ParseFunctionSpecArgumentNode(FunctionSpec *funcSpec, MSXML2::IXMLDOMNodePtr &argNode, int argIndex);
    void ParseVTableSpecNode(MSXML2::IXMLDOMNodePtr &vtSpecNode);

    void ParseDllModuleNode(MSXML2::IXMLDOMNodePtr &dllModNode);
    void ParseDllFunctionNode(DllModule *dllMod, MSXML2::IXMLDOMNodePtr &dllFuncNode);
    void ParseVTableNode(MSXML2::IXMLDOMNodePtr &vtNode);
};

// TODO: refactor the mess below

class EnumerationBuilder
{
public:
    static EnumerationBuilder *Instance();
    ~EnumerationBuilder();

    void AddEnumeration(Marshaller::Enumeration *enumTemplate);

    unsigned int GetEnumerationCount() const { return static_cast<unsigned int>(m_enumTypes.size()); }

protected:
    typedef OMap<OString, Marshaller::Enumeration *>::Type EnumTypeMap;
    EnumTypeMap m_enumTypes;

    static BaseMarshaller *BuildEnumerationWrapper(const OString &name);
    BaseMarshaller *BuildEnumeration(const OString &name);
};

class StructureDef
{
public:
    StructureDef(const OString &name) { m_name = name; }
    ~StructureDef();

    const OString &GetName() const { return m_name; }

    void AddField(StructureFieldDef *field) { m_fields.push_back(field); }

    Marshaller::Structure *CreateInstance() const;

protected:
    OString m_name;

    typedef OVector<StructureFieldDef *>::Type FieldDefList;
    FieldDefList m_fields;
};

class StructureFieldDef
{
public:
    StructureFieldDef(const OString &name, DWORD offset, const OString &typeName);

    Marshaller::StructureField *CreateInstance();

protected:
    OString m_name;
    DWORD m_offset;
    OString m_typeName;
};

class StructureBuilder
{
public:
    static StructureBuilder *Instance();
    ~StructureBuilder();

    void AddStructure(StructureDef *structDef);

    unsigned int GetStructCount() const { return static_cast<unsigned int>(m_structDefs.size()); }

protected:
    typedef OMap<OString, StructureDef *>::Type StructDefMap;
    StructDefMap m_structDefs;

    static BaseMarshaller *BuildStructureWrapper(const OString &name);
    BaseMarshaller *BuildStructure(const OString &name);

    static BaseMarshaller *BuildStructurePtrWrapper(const OString &name);
    BaseMarshaller *BuildStructurePtr(const OString &name);
};

} // namespace InterceptPP