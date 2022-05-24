using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;

namespace ProtoDumper.Outputs {
    public class BaseDumper {
        private List<Proto> Protos;
        private string OutputFolder;
        private bool DumpCmdIdEnums;

        public BaseDumper(List<Proto> protos, string outputFolder, bool dumpCmdIdEnums = false) {
            Protos = protos;
            OutputFolder = outputFolder;
            DumpCmdIdEnums = dumpCmdIdEnums;
        }

        public virtual string BuildFile(Proto proto) {
            var stringBuilder = new StringBuilder();

            stringBuilder.AppendLine("syntax = \"proto3\";");
            stringBuilder.AppendLine();
            foreach (var line in DumpProto(proto)) {
                stringBuilder.AppendLine(line);
            }

            return stringBuilder.ToString();
        }

        public virtual List<string> DumpProto(Proto proto) {
            var strings = new List<string>();

            var imports = new List<string>();

            // TODO: Make a decent way to get imports because this..................
            foreach (var field in proto.Fields) {
                foreach (var type in field.Types) {
                    if (type.IsImport) imports.Add(type.Name);
                }
            }

            foreach (var nestedProto in proto.NestedProtos) {
                foreach (var field in nestedProto.Fields) {
                    foreach (var type in field.Types) {
                        if (type.IsImport) imports.Add(type.Name);
                    }
                }
            }

            foreach (var oneof in proto.Oneofs) {
                foreach (var entry in oneof.Entries) {
                    if (entry.IsImport) imports.Add(entry.Type);
                }
            }

            if (!proto.Nested) {
                foreach (var import in imports.Distinct().ToList()) {
                    strings.Add($"import \"{import}.proto\";");
                }
                if (imports.Count > 0) strings.Add("");
            }

            strings.Add($"{(proto.IsEnum ? "enum" : "message")} {proto.Name} {{");

            foreach (var nestedProto in proto.NestedProtos) {
                foreach (var line in DumpProto(nestedProto)) {
                    strings.Add("  " + line);
                }
            }

            foreach (var pEnum in proto.Enums) {
                if (pEnum.Name == "CmdId" && !DumpCmdIdEnums) continue;
                foreach (var line in DumpEnum(pEnum)) {
                    strings.Add("  " + line);
                }
            }

                foreach (var oneof in proto.Oneofs) {
                foreach (var line in DumpOneof(oneof)) {
                    strings.Add("  " + line);
                }
            }

            foreach (var field in proto.Fields) {
                if (field.IsRepeated) {
                    strings.Add($"  repeated {field.Types[0].Name}{(field.Name != "" ? $" {ToSnakeCase(field.Name)}" : "")} = {field.FieldNumber};");
                }
                else if (field.IsMap) {
                    strings.Add($"  map<{field.Types[0].Name}, {field.Types[1].Name}>{(field.Name != "" ? $" {ToSnakeCase(field.Name)}" : "")} = {field.FieldNumber};");
                }
                else {
                    strings.Add($"  {(proto.IsEnum ? ToSnakeCase(field.Types[0].Name).ToUpper() : field.Types[0].Name)}{(field.Name != "" ? $" {ToSnakeCase(field.Name)}" : "")} = {field.FieldNumber};");
                }
            }

            strings.Add("}");

            return strings;
        }

        public virtual List<string> DumpEnum(ProtoEnum pEnum) {
            var strings = new List<string>();

            strings.Add($"enum {pEnum.Name} {{");

            // TODO: Check if it has aliases, if it doesn't, don't add this line
            // TODO: Fix CmdId for protoc
            if (pEnum.Name.Equals("CmdId")) strings.Add("  option allow_alias = true;");

            foreach (var entry in pEnum.Entries) {
                strings.Add($"  {ToSnakeCase(entry.Name).ToUpper()} = {entry.Value};");
            }
            strings.Add("}");

            return strings;
        }

        public virtual List<string> DumpOneof(ProtoOneof oneof) {
            var strings = new List<string>();

            strings.Add($"oneof {oneof.Name} {{");
            foreach (var entry in oneof.Entries) {
                strings.Add($"  {entry.Type} {ToSnakeCase(entry.Name)} = {entry.FieldNumber};");
            }
            strings.Add("}");

            return strings;
        }

        public void Dump(string fileExtension) {
            var packetIds = new Dictionary<string, int>();

            foreach (var proto in Protos) {
                DumpToFolder(Path.Combine(OutputFolder, $"{proto.Name}.{fileExtension}"), BuildFile(proto));
                if (proto.CmdID != 0) packetIds.Add(proto.Name, proto.CmdID);
            }

            var orderedPackets = packetIds.OrderBy(p => p.Value).ToList();

            var writer = new StreamWriter(Path.Combine(OutputFolder, "packetIds.json"));
            var writerReversed = new StreamWriter(Path.Combine(OutputFolder, "packetIdsReversed.json"));

            writer.WriteLine("{");
            writerReversed.WriteLine("{");

            for (int i = 0; i < orderedPackets.Count; i++) {
                var packet = orderedPackets[i];
                writer.WriteLine($"  \"{packet.Key}\": \"{packet.Value}\"{((i + 1) != orderedPackets.Count ? "," : "")}");
                // Exclude DebugNotify because it's ID is 101 and an actual proto uses that ID
                if (packet.Key != "DebugNotify") writerReversed.WriteLine($"  \"{packet.Value}\": \"{packet.Key}\"{((i + 1) != orderedPackets.Count ? "," : "")}");
            }

            writer.WriteLine("}");
            writerReversed.WriteLine("}");

            writer.Close();
            writerReversed.Close();
        }

        public void DumpToFolder(string path, string content) {
            var writer = new StreamWriter(path);
            writer.Write(content);
            writer.Close();
        }

        public string ToCamelCase(string str) => string.IsNullOrEmpty(str) || str.Length < 2 ? str : char.ToLowerInvariant(str[0]) + str.Substring(1);
        public string ToSnakeCase(string text) {
            if (text.Length < 2) {
                return text;
            }
            var sb = new StringBuilder();
            sb.Append(char.ToLowerInvariant(text[0]));
            for (int i = 1; i < text.Length; ++i) {
                char c = text[i];
                if (char.IsUpper(c)) {
                    sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
