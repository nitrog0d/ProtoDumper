using Mono.Cecil;
using System;
using System.Collections.Generic;

namespace ProtoDumper {
    public class ProtoParser {

        private string AssemblyPath;
        private ModuleDefinition Module;

        public ProtoParser(string assemblyPath) {
            AssemblyPath = assemblyPath;
        }

        public List<Proto> Parse() {
            // Read the assembly
            Module = ModuleDefinition.ReadModule(AssemblyPath);

            // Find the Proto base class
            var protoBase = Module.GetType("Google.Protobuf.MessageBase");

            if (protoBase == null) {
                return null;
            }

            var protos = new List<Proto>();

            // Loop through all types that have a base type of the proto base
            foreach (var type in Module.GetTypes()) {
                // TODO: Implement a decent proto detection in case of obfuscated assemblies
                if (type.Namespace == "Proto") {
                    protos.Add(TypeToProto(type));
                }
            }

            return protos;
        }

        private const string RepeatedPrimitiveFieldName = "Google.Protobuf.Collections.RepeatedPrimitiveField`1";
        private const string RepeatedMessageFieldName = "Google.Protobuf.Collections.RepeatedMessageField`1";
        private const string MapFieldName = "Google.Protobuf.Collections.MapField`2";
        private const string MessageMapFieldName = "Google.Protobuf.Collections.MessageMapField`2";

        public Proto TypeToProto(TypeDefinition type, bool nested = false) {
            var properties = type.Properties;

            var cmdId = 0;

            var protoFields = new List<ProtoField>();
            var protoEnums = new List<ProtoEnum>();
            var nestedProtos = new List<Proto>();
            var protoOneofs = new List<ProtoOneof>();
            var fieldsToExclude = new List<string>();

            // Loop through all Nested types
            foreach (var nestedType in type.NestedTypes) {
                // If nested type is an oneof
                if (nestedType.Name.EndsWith("OneofCase")) {
                    var protoOneofEntries = new List<ProtoOneofEntry>();
                    foreach (var field in nestedType.Fields) {
                        if (field.Name != "value__" && field.Name != "None") {
                            PropertyDefinition foundProperty = null;

                            // Find the property from the entry name to get the entry type
                            foreach (var property in properties) {
                                if (property.Name == field.Name) {
                                    foundProperty = property;
                                    break;
                                }
                            }

                            protoOneofEntries.Add(new ProtoOneofEntry(CSharpTypeNameToProtoTypeName(foundProperty.PropertyType), field.Name, (int)field.Constant, foundProperty.PropertyType.Namespace == "Proto"));
                            // Exclude Oneof fields
                            fieldsToExclude.Add(foundProperty.Name);
                        }
                    }
                    protoOneofs.Add(new ProtoOneof(nestedType.Name.Substring(0, nestedType.Name.Length - 9), protoOneofEntries));
                    // If type is a nested proto or cmd id
                }
                else if (nestedType.Name == "Types") {
                    foreach (var nestedType2 in nestedType.NestedTypes) {
                        if (nestedType2.BaseType.FullName == "System.Enum") {
                            var protoEnumContents = new List<ProtoEnumEntry>();
                            foreach (var field in nestedType2.Fields) {
                                if (field.Name != "value__" && field.Name != "None") {
                                    // If the field is named CmdId, set the CmdId from it
                                    // if (field.Name == "CmdId" && nestedType2.Name != "CmdId") Console.WriteLine("Whoops wtf is this one: " + type.FullName); // It was DebugNotify
                                    if (field.Name == "CmdId") cmdId = (int)field.Constant;
                                    protoEnumContents.Add(new ProtoEnumEntry(field.Name, (int)field.Constant));
                                }
                            }
                            protoEnums.Add(new ProtoEnum(nestedType2.Name, protoEnumContents));
                        }
                        else {
                            nestedProtos.Add(TypeToProto(nestedType2, true));
                        }
                    }
                }
            }

            var isEnum = type.BaseType.FullName == "System.Enum";

            // Loop through all fields and find the proto fields
            foreach (var field in type.Fields) {
                if (field.HasConstant && field.Name.EndsWith("FieldNumber")) {
                    PropertyDefinition foundProperty = null;

                    // Find the property from the field name to get the field type
                    foreach (var property in properties) {
                        if (property.Name == field.Name.Substring(0, field.Name.Length - 11)) {
                            foundProperty = property;
                            break;
                        }
                    }

                    if (foundProperty != null) {
                        ProtoField protoField = null;
                        // If the field is a repeated primitive or repeated message
                        if (foundProperty.PropertyType.FullName.StartsWith(RepeatedPrimitiveFieldName) || foundProperty.PropertyType.FullName.StartsWith(RepeatedMessageFieldName)) {
                            var mapType = (GenericInstanceType)foundProperty.PropertyType;
                            protoField = new ProtoField(new List<ProtoType>() { new ProtoType(CSharpTypeNameToProtoTypeName(mapType.GenericArguments[0]), mapType.GenericArguments[0].Namespace == "Proto") }, foundProperty.Name, (int)field.Constant, true);
                            // fieldType = $"repeated {CSharpTypeNameToProtoTypeName(mapType.GenericArguments[0])}";
                        }
                        // If the field is a map or message map
                        else if (foundProperty.PropertyType.FullName.StartsWith(MapFieldName) || foundProperty.PropertyType.FullName.StartsWith(MessageMapFieldName)) {
                            var mapType = (GenericInstanceType)foundProperty.PropertyType;
                            protoField = new ProtoField(new List<ProtoType>() { new ProtoType(CSharpTypeNameToProtoTypeName(mapType.GenericArguments[0]), mapType.GenericArguments[0].Namespace == "Proto"), new ProtoType(CSharpTypeNameToProtoTypeName(mapType.GenericArguments[1]), mapType.GenericArguments[1].Namespace == "Proto") }, foundProperty.Name, (int)field.Constant, false, true);
                            // fieldType = $"map<{CSharpTypeNameToProtoTypeName(mapType.GenericArguments[0])}, {CSharpTypeNameToProtoTypeName(mapType.GenericArguments[1])}>";
                        }
                        else {
                            // If the field is a proto, add to import list
                            protoField = new ProtoField(new List<ProtoType>() { new ProtoType(CSharpTypeNameToProtoTypeName(foundProperty.PropertyType), foundProperty.PropertyType.Namespace == "Proto") }, foundProperty.Name, (int)field.Constant);
                            // fieldType = CSharpTypeNameToProtoTypeName(foundProperty.PropertyType);
                        }
                        // Override for fixed32 in some protos
                        if ((type.Name == "HomeVerifyData" && (foundProperty.Name == "Timestamp")) ||
                            (type.Name == "HomePlantSubFieldData" && (foundProperty.Name == "EndTime")) ||
                            (type.Name == "AbilityEmbryo" && (foundProperty.Name == "AbilityNameHash" || foundProperty.Name == "AbilityOverrideNameHash")) ||
                            (type.Name == "HomeResource") && (foundProperty.Name == "NextRefreshTime") ||
                            (type.Name == "HomePriorCheckNotify") && (foundProperty.Name == "EndTime") ||
                            (type.Name == "FurnitureMakeBeHelpedData") && (foundProperty.Name == "Time") ||
                            (type.Name == "HomeLimitedShopInfo") && (foundProperty.Name == "NextOpenTime" || foundProperty.Name == "NextGuestOpenTime" || foundProperty.Name == "NextCloseTime") ||
                            (type.Name == "FurnitureMakeData") && (foundProperty.Name == "BeginTime" || foundProperty.Name == "AccelerateTime")) {
                            protoField = new ProtoField(new List<ProtoType>() { new ProtoType("fixed32") }, foundProperty.Name, (int)field.Constant);
                        }
                        if (!fieldsToExclude.Contains(foundProperty.Name)) protoFields.Add(protoField);
                    }
                }

                // If the type is an enum
                if (isEnum) {
                    if (field.Name != "value__") {
                        protoFields.Add(new ProtoField(new List<ProtoType>() { new ProtoType(field.Name) }, "", (int)field.Constant));
                    }
                }
            }

            return new Proto(type.Name, cmdId, protoFields, protoEnums, nestedProtos, protoOneofs, nested, isEnum);
        }

        // So far all the used types
        public static Dictionary<string, string> ProtoTypes = new Dictionary<string, string> {
            ["System.UInt32"] = "uint32",
            ["System.UInt64"] = "uint64",
            ["System.Boolean"] = "bool",
            ["System.Int32"] = "int32",
            ["System.Int64"] = "int64",
            ["System.String"] = "string",
            ["System.Single"] = "float",
            ["System.Double"] = "double",
            ["Google.Protobuf.ByteString"] = "bytes"
        };

        // Converts CSharp type names to Proto type names
        public static string CSharpTypeNameToProtoTypeName(TypeReference type) {
            if (type.Namespace == "Proto" || type.IsNested) return type.Name;
            if (ProtoTypes.TryGetValue(type.FullName, out var proto)) {
                return proto;
            }
            else {
                Console.WriteLine($"Unknown type \"{type.FullName}\" found!");
                return $"UNK_{type}";
            }
        }
    }
}
