# VASharp: .NET Bindings for libva

## Building on Ubuntu

First, install .NET:

```
apt-get install -y dotnet-sdk-10.0
```

Then, install libva:

```
apt-get install -y --no-install-recommends unzip libva-dev libstdc++-13-dev
```

Install ClangSharpPInvokeGenerator to generate the C# bindings:

```
dotnet tool install --global ClangSharpPInvokeGenerator --version 18.1.0.3
mkdir lib && cd lib
curl -X GET -L https://www.nuget.org/api/v2/package/libclang.runtime.linux-x64/18.1.3.2 --output libclang.zip
curl -X GET -L https://www.nuget.org/api/v2/package/libClangSharp.runtime.linux-x64/18.1.3.1 --output libClangSharp.zip
unzip libclang.zip
unzip -o libClangSharp.zip
mv runtimes/linux-x64/native/libclang.so runtimes/linux-x64/native/libclang.so.18
cd ../VASharp/Native
LD_LIBRARY_PATH="../../lib/runtimes/linux-x64/native/" ~/.dotnet/tools/ClangSharpPInvokeGenerator @libva.rsp
LD_LIBRARY_PATH="../../lib/runtimes/linux-x64/native/" ~/.dotnet/tools/ClangSharpPInvokeGenerator @libva-drm.rsp
```

## Building on Windows

First, install vcpkg:

```
git clone https://github.com/microsoft/vcpkg.git
cd vcpkg; .\bootstrap-vcpkg.bat
```

Next, build libva:

```
..\vcpkg\vcpkg.exe install
```

Then, generate the C# bindings for libva:

```
dotnet tool install ClangSharpPInvokeGenerator --version 18.1.0.3
cd VASharp/Native
dotnet tool run ClangSharpPInvokeGenerator @libva2.rsp
dotnet tool run ClangSharpPInvokeGenerator @libva-win32.rsp
```

You can download the Windows drivers (dxil.dll and vaon12_drv_video.dll) at https://www.nuget.org/packages/Microsoft.Direct3D.VideoAccelerationCompatibilityPack/.