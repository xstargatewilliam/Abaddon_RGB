; Abaddon RGB — instalador (Inno Setup 6)
; SPDX-License-Identifier: GPL-3.0-or-later
;
; Instala para todos os usuários em C:\Program Files\AbaddonRGB.
; A pasta é fixa porque o programa usa esse mesmo caminho como "cópia protegida"
; do modo administrador (StartupManager.ProtectedExe): instalado ali, existe uma cópia só.
;
; Compilar (a automação do GitHub faz isso sozinha):
;   ISCC.exe /DMyAppVersion=0.5.7 installer\AbaddonRGB.iss
; Espera o executável publicado em publish\AbaddonRGB.exe.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define MyAppName "Abaddon RGB"
#define MyAppExe "AbaddonRGB.exe"

[Setup]
; Não mude o AppId: é por ele que o Windows reconhece instalações e atualizações anteriores.
AppId={{2EBE7F89-F592-473F-8037-612B97C13BBE}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=William Stargate
AppPublisherURL=https://github.com/xstargatewilliam/Abaddon_RGB
AppSupportURL=https://github.com/xstargatewilliam/Abaddon_RGB/issues
AppUpdatesURL=https://github.com/xstargatewilliam/Abaddon_RGB/releases
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\AbaddonRGB
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\publish
OutputBaseFilename=AbaddonRGB-Setup
SetupIconFile=..\src\AbaddonRGB\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Fecha o programa aberto antes de instalar ou desinstalar (mesmo nome do Program.cs).
AppMutex=AbaddonRGB_SingleInstance
CloseApplications=yes
RestartApplications=no
; A desinstalação apaga a inicialização do usuário (HKCU). É intencional.
UsedUserAreasWarning=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
brazilianportuguese.FinishedLabel=O Abaddon RGB foi instalado.%n%nPara a placa-mãe, as memórias e o cooler ele usa o OpenRGB: instale o OpenRGB e ligue o servidor (aba "SDK Server" → "Start Server"). O teclado Logitech funciona sem o OpenRGB, pela aba Sistema.

[Tasks]
Name: "desktopicon"; Description: "Criar um atalho na área de trabalho"; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
; Só para remover na desinstalação a inicialização com o Windows (o programa cria esse valor).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "AbaddonRGB"; ValueType: none; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Abrir o Abaddon RGB agora"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Inicialização como administrador (tarefa agendada criada pelo programa).
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""AbaddonRGB"" /F"; Flags: runhidden; RunOnceId: "RemoverTarefa"

[UninstallDelete]
; Sobras da atualização automática.
Type: files; Name: "{app}\{#MyAppExe}.old"
Type: files; Name: "{app}\{#MyAppExe}.download"
Type: files; Name: "{app}\{#MyAppExe}.failed"
Type: files; Name: "{app}\{#MyAppExe}.new"
Type: dirifempty; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Na desinstalação silenciosa, a resposta padrão é manter.
    if SuppressibleMsgBox('Apagar também as suas configurações, perfis e registros do Abaddon RGB?' + #13#10 +
                          '(Escolha "Não" se pretende instalar de novo.)',
                          mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      DelTree(ExpandConstant('{userappdata}\AbaddonRGB'), True, True, True);
  end;
end;
