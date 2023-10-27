del /q /s .\dist
dotnet publish -r win-x64 -c Release -o dist
.\dist\Example.NativeAot.exe
