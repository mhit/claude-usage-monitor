; Claude Usage Monitor インストーラ
; NSIS 3.x Unicode

!include "MUI2.nsh"
!include "FileFunc.nsh"

; アプリケーション情報
!define APPNAME "Claude Usage Monitor"
!define COMPANYNAME "mhit"
!define DESCRIPTION "Claude.ai 使用状況モニター"
!define VERSIONMAJOR 1
!define VERSIONMINOR 0
!define VERSIONBUILD 1
!define HELPURL "https://github.com/mhit/claude-usage-monitor"
!define ABOUTURL "https://github.com/mhit/claude-usage-monitor"

; インストーラ設定
Name "${APPNAME}"
OutFile "ClaudeUsageMonitor-Setup-v${VERSIONMAJOR}.${VERSIONMINOR}.${VERSIONBUILD}.exe"
InstallDir "$LOCALAPPDATA\${APPNAME}"
InstallDirRegKey HKCU "Software\${COMPANYNAME}\${APPNAME}" "InstallDir"
RequestExecutionLevel user
Unicode True

; MUI設定
!define MUI_ABORTWARNING
!define MUI_ICON "${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico"
!define MUI_UNICON "${NSISDIR}\Contrib\Graphics\Icons\modern-uninstall.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${NSISDIR}\Contrib\Graphics\Wizard\win.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${NSISDIR}\Contrib\Graphics\Wizard\win.bmp"

; 言語設定
!define MUI_LANGDLL_ALLLANGUAGES

; インストーラページ
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "LICENSE.txt"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\ClaudeUsageMonitor.exe"
!define MUI_FINISHPAGE_RUN_TEXT "今すぐ起動する"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Windows起動時に自動起動する"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION AddToStartup
!insertmacro MUI_PAGE_FINISH

; アンインストーラページ
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; 言語ファイル
!insertmacro MUI_LANGUAGE "Japanese"
!insertmacro MUI_LANGUAGE "English"

; 言語選択
Function .onInit
  !insertmacro MUI_LANGDLL_DISPLAY
FunctionEnd

; インストールセクション
Section "インストール" SecInstall
  SetOutPath "$INSTDIR"
  
  ; ファイルをコピー
  File /r "build\*.*"
  
  ; スタートメニューショートカット
  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\ClaudeUsageMonitor.exe" "" "$INSTDIR\ClaudeUsageMonitor.exe" 0
  CreateShortcut "$SMPROGRAMS\${APPNAME}\アンインストール.lnk" "$INSTDIR\uninstall.exe"
  
  ; デスクトップショートカット
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\ClaudeUsageMonitor.exe" "" "$INSTDIR\ClaudeUsageMonitor.exe" 0
  
  ; レジストリに書き込み
  WriteRegStr HKCU "Software\${COMPANYNAME}\${APPNAME}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\${COMPANYNAME}\${APPNAME}" "Version" "${VERSIONMAJOR}.${VERSIONMINOR}.${VERSIONBUILD}"
  
  ; アンインストール情報
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayIcon" "$INSTDIR\ClaudeUsageMonitor.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "Publisher" "${COMPANYNAME}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "HelpLink" "${HELPURL}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "URLInfoAbout" "${ABOUTURL}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "${VERSIONMAJOR}.${VERSIONMINOR}.${VERSIONBUILD}"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "VersionMajor" ${VERSIONMAJOR}
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "VersionMinor" ${VERSIONMINOR}
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoRepair" 1
  
  ; サイズ計算
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "EstimatedSize" "$0"
  
  ; アンインストーラ作成
  WriteUninstaller "$INSTDIR\uninstall.exe"
SectionEnd

; 自動起動追加
Function AddToStartup
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APPNAME}" '"$INSTDIR\ClaudeUsageMonitor.exe"'
FunctionEnd

; アンインストールセクション
Section "Uninstall"
  ; プロセス終了
  nsExec::ExecToLog 'taskkill /F /IM ClaudeUsageMonitor.exe'
  
  ; ファイル削除
  RMDir /r "$INSTDIR"
  
  ; ショートカット削除
  Delete "$DESKTOP\${APPNAME}.lnk"
  RMDir /r "$SMPROGRAMS\${APPNAME}"
  
  ; レジストリ削除
  DeleteRegKey HKCU "Software\${COMPANYNAME}\${APPNAME}"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APPNAME}"
  
  ; キャッシュ削除確認
  MessageBox MB_YESNO|MB_ICONQUESTION "設定ファイルとキャッシュも削除しますか？" IDNO skip_cache
    RMDir /r "$LOCALAPPDATA\ClaudeUsageMonitor"
  skip_cache:
SectionEnd
