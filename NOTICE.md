# Créditos e licenças

O Abaddon RGB é distribuído sob a **GNU General Public License v3.0 ou posterior** (arquivo `LICENSE`).

## Código portado

- **OpenRGB** — https://gitlab.com/CalcProgrammer1/OpenRGB — Copyright (C) OpenRGB contributors, **GPL-2.0-or-later**.
  Partes do driver Logitech HID++ 2.0 (`Controllers/LogitechController/LogitechHIDPP20Controller` e
  `RGBController_LogitechHIDPP20`) foram portadas para C# em `src/AbaddonRGB.Core/Engines/Native/`:
  formato das mensagens HID++ 2.0, consulta de recursos (IRoot), recurso 0x8080 (cor por tecla),
  pareamento das coleções HID e a tabela de teclas reais de cada modelo. Cada arquivo portado indica a origem no cabeçalho.
  Por ser GPL-2.0-or-later, esse código pode ser usado sob a GPL-3.0, que é a licença deste projeto.

## Bibliotecas

| Biblioteca | Licença |
|---|---|
| OpenRGB.NET | MIT |
| HidSharp | Apache-2.0 |
| LibreHardwareMonitorLib | MPL-2.0 |

## Arte e fontes

- Fonte **Oxanium** — SIL Open Font License 1.1 (`src/AbaddonRGB/Assets/Oxanium-OFL.txt`).
- Logo, fundo, ícones e ilustrações do gabinete: arte do próprio projeto.
