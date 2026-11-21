Unicode true
!include "MUI2.nsh"
Name "Chlorella"
OutFile "Chlorella-${APP_VERSION}-win-x64-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\Chlorella"
RequestExecutionLevel user
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=1041 "ProductName" "Chlorella"
VIAddVersionKey /LANG=1041 "FileDescription" "Chlorella Setup"
VIAddVersionKey /LANG=1041 "FileVersion" "${APP_VERSION}"
!define MUI_FINISHPAGE_RUN "$INSTDIR\app\Chlorella.exe"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "Japanese"
Section "Chlorella"
  SetOutPath "$INSTDIR\app"
  File /r "${SOURCE_DIR}\*"
  CreateShortcut "$SMPROGRAMS\Chlorella.lnk" "$INSTDIR\app\Chlorella.exe"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Chlorella" "DisplayName" "Chlorella"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Chlorella" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Chlorella" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Chlorella" "DisplayIcon" "$INSTDIR\app\Chlorella.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Chlorella" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Chlorella" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Chlorella" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Chlorella" "NoRepair" 1
SectionEnd
Section "Uninstall"
  Delete "$SMPROGRAMS\Chlorella.lnk"
  RMDir /r "$INSTDIR\app"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Chlorella"
SectionEnd
