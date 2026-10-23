@echo off
setlocal

for %%X in (dotnet.exe) do (set DOTNET_IN_PATH=%%~$PATH:X)
if not defined DOTNET_IN_PATH goto error_dotnet

dotnet build ..\PSDPlugin.sln -c Release --nologo
"C:\Program Files\7-Zip\7z.exe" a PSDPlugin.zip ..\PhotoShopFileType\bin\Release\PhotoShop.dll ..\License.txt ..\Readme.txt
goto end

:error_dotnet
echo dotnet.exe not found in the PATH.
goto end

:end
endlocal
