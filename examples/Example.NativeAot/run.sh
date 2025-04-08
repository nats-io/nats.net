rm -rf ./dist
dotnet publish -r linux-x64 -c Release -o dist
./dist/Example.NativeAot
