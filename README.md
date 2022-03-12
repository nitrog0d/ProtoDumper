# ProtoDumper
This program dumps Protocol Buffer definitions from game assemblies, they are used for own made servers or packet analyzing.  
### **It requires unobfuscated assemblies** as I still didn't implement obfuscated assembly support.

## Usage

Build it using Visual Studio 2022  
Open the .exe file and choose the assembly with proto definitions (Assembly-CSharp.dll), alternatively use command line

### Command Line Reference
```
Usage: ProtoDumper [parameters]
Possible parameters:
        --help, -h - Optional. Show this help
        --dont-delete-old-protos - Optional. Stop the program from deleting old protos
        --assembly-path= - Optional. Path to the assembly that contains protos
        --output-path= - Optional. Output path
        --export-type= - Optional. Export type, can be typescript or proto
        --export-file-extension= - Optional. File extension used in the exported files
        --proto-base= - Optional. Base class for protos
        --repeated-message-field-class= - Optional. Base class for repeated message fields
```

## Credits

[Il2CppAssemblyUnhollower](https://github.com/knah/Il2CppAssemblyUnhollower): Command line parameters and help is based on it, thanks [knah](https://github.com/knah)  
[Mono.Cecil](https://github.com/jbevain/cecil): Used to read the assembly
