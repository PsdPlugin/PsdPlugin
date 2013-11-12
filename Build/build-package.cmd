@echo off
setlocal

for %%X in (MSBuild.exe) do (set MSBUILD_IN_PATH=%%~$PATH:X)
if not defined MSBUILD_IN_PATH goto error_msbuild

msbuild ..\PSDPlugin.sln /t:Rebuild /p:Configuration=Release
"C:\Program Files\7-Zip\7z.exe" a PSDPlugin.zip ..\PhotoShopFileType\bin\Release\PhotoShop.dll ..\License.txt ..\Readme.txt
goto end

:error_msbuild
echo MSBuild.exe not found in the PATH.
goto end

:end
endlocal
