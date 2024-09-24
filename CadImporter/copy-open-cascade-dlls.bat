@echo off
set "OCCT=C:\OpenCASCADE-7.8.0-vc143-64\occt-vc143-64\win64\vc14\bin"
set "OCCT_THIRD_PARTY=C:\OpenCASCADE-7.8.0-vc143-64\3rdparty-vc14-64"
set "TARGET_DIR=OpenCascade"

echo Check that the paths in the following variables are correctly set:
echo:
echo OCCT: %OCCT%
echo OCCT_THIRD_PARTY: %OCCT_THIRD_PARTY%
echo TARGET_DIR: %TARGET_DIR%
echo:
echo If not, then open the copy-open-cascade-dlls.bat and correct the paths.
echo:

pause

xcopy "%OCCT%\LICENSE_LGPL_21.txt" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\OCCT_LGPL_EXCEPTION.txt" "%TARGET_DIR%\" /Y

xcopy "%OCCT%\TKernel.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKXSBase.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKDESTEP.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKDEIGES.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKG3d.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKMath.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKBRep.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKLCAF.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKMesh.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKBinXCAF.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKGeomBase.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKXCAF.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKShHealing.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKTopAlgo.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKG2d.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKDE.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKCDF.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKBin.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKService.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKBinL.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKCAF.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKVCAF.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKV3d.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKGeomAlgo.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKBO.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKHLR.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKBool.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKPrim.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT%\TKVCAF.dll" "%TARGET_DIR%\" /Y

rem The followig can be skipped when ImporterSettings.CalculateShapeProperties is false
xcopy "%OCCT%\TKTopAlgo.dll" "%TARGET_DIR%\" /Y

xcopy "%OCCT_THIRD_PARTY%\tbb2021.5-vc14-x64\bin\tbb12.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT_THIRD_PARTY%\tbb2021.5-vc14-x64\bin\tbbmalloc.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT_THIRD_PARTY%\jemalloc-vc14-64\bin\jemalloc.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT_THIRD_PARTY%\openvr-1.14.15-64\bin\win64\openvr_api.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT_THIRD_PARTY%\freeimage-3.17.0-vc14-64\bin\FreeImage.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT_THIRD_PARTY%\freetype-2.5.5-vc14-64\bin\freetype.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT_THIRD_PARTY%\ffmpeg-3.3.4-64\bin\avcodec-57.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT_THIRD_PARTY%\ffmpeg-3.3.4-64\bin\avformat-57.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT_THIRD_PARTY%\ffmpeg-3.3.4-64\bin\swscale-4.dll" "%TARGET_DIR%\" /Y
xcopy "%OCCT_THIRD_PARTY%\ffmpeg-3.3.4-64\bin\avutil-55.dll" "%TARGET_DIR%\" /Y


pause