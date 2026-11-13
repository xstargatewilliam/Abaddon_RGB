# Abaddon RGB

Programa para Windows que controla as luzes RGB do PC inteiro com uma cor só: placa-mãe, cooler, memórias, teclado e mouse.
Ele usa o [OpenRGB](https://openrgb.org) como "motor": o OpenRGB conversa com o hardware, e o Abaddon RGB manda as cores para ele pelo servidor SDK (porta 6742).

## O que o programa faz

A interface segue o Design System v1.0 do Abaddon RGB (tema cósmico escuro, roxo e magenta, fonte Oxanium nos títulos): menu lateral, roda de cores, prévia do gabinete na cor atual e rodapé com a temperatura da CPU e da placa de vídeo.

**Cores**
- Uma cor geral para tudo (seletor, hexadecimal ou cores rápidas) e brilho de 0 a 100%.
- Cor própria por peça ou por zona: o cooler de uma cor, a placa de outra, as memórias de outra.
- "Não controlar esta peça": se uma peça der problema, o programa para de enviar comandos para ela.

**Perfis**
- Guarda a cor geral, o brilho e as cores próprias com um nome (já vem com Dia, Noite e Apagado).
- Os perfis aparecem no menu do ícone perto do relógio.

**Automações**
- **Por horário:** "às 22:00 aplicar Noite, às 07:00 aplicar Dia". Ao abrir o programa (ou voltar da suspensão), ele aplica o perfil que deveria estar valendo naquela hora.
- **Por temperatura:** escolhe um sensor (CPU ou placa de vídeo), uma cor para frio e outra para quente, e a peça que vai mostrar isso (tudo, uma peça ou uma zona, como o cooler). A cor vai mudando aos poucos entre as duas.

**Sistema**
- Fica na bandeja do Windows; o ícone mostra a cor atual.
- Inicia com o Windows (opcional), escondido, e reaplica as cores. Também reaplica ao voltar da suspensão.
- Se atualiza sozinho pelas releases do GitHub.
- Modo somente leitura, registro do que foi feito e detecção nativa de teste (base para funcionar sem o OpenRGB: veja o [ROADMAP](ROADMAP.md)).

### Sobre a temperatura

- A leitura usa a biblioteca LibreHardwareMonitor, só com CPU e placa de vídeo ligadas.
- **Placa de vídeo:** costuma funcionar sem nada extra.
- **CPU (Ryzen):** o Windows só libera com o **PawnIO** instalado e o Abaddon RGB aberto **como administrador**. Sem isso o sensor aparece "sem leitura". Isso se resolve de vez na Fase 4 (serviço do Windows).

## Hardware testado

| Peça | Como aparece no OpenRGB | Observação |
|---|---|---|
| ASRock B450M Steel Legend | ASRock B450M Steel Legend | Controlada pelo SMBus (precisa do PawnIO) |
| Water cooler Rise Mode (ARGB) | zona "Addressable Header" da placa | 1 cor por vez. No modo "Static" do OpenRGB as cores saem trocadas, mas pelo Abaddon RGB saem certas (sem correção) |
| 2× RAM Asgard RGB | ENE DRAM | Não salva a cor no dispositivo |
| Teclado Logitech G512 | G512 RGB MECHANICAL GAMING KEYBOARD | Feche o G HUB |
| Mouse Logitech G305 | G305 Lightspeed Wireless Gaming Mouse | Feche o G HUB |

## Como usar

1. Instale o **OpenRGB 1.1 ou mais novo** e o **PawnIO** (pawnio.eu). Confirme que o OpenRGB enxerga suas peças.
2. Deixe o **servidor SDK** do OpenRGB ligado:
   - se você instalou o OpenRGB **como serviço**, o servidor já roda sozinho;
   - se não, abra o OpenRGB, vá na aba **Servidor SDK** e clique em **Iniciar servidor**. Em Configurações dá para marcar para ele iniciar com o Windows já com o servidor ligado.
3. Feche o **G HUB** e outros apps de RGB (Polychrome, iCUE etc.) para não brigarem pelas cores.
4. Baixe o `AbaddonRGB.exe` na página de **Releases** do repositório (ou em **Actions** → último build → *AbaddonRGB-win-x64*) e abra. Não precisa instalar o .NET.

## Configurações

Ficam em `%APPDATA%\AbaddonRGB\config.json` (botão **Abrir configurações**):

```json
{
  "Host": "127.0.0.1",
  "Port": 6742,
  "Color": "#3A6EA5",
  "Brightness": 100,
  "ApplyOnStart": true,
  "AutoUpdate": true,
  "ReadOnly": false,
  "Overrides": [ { "Device": "ASRock B450M Steel Legend", "Zone": "Addressable Header", "Color": "#00FF40" } ],
  "DisabledDevices": [],
  "Profiles": [ { "Name": "Noite", "Color": "#FF6A00", "Brightness": 25, "Overrides": [] } ],
  "Schedule": { "Enabled": true, "Rules": [ { "Time": "22:00", "Profile": "Noite" } ] },
  "Temperature": { "Enabled": false, "SensorId": "", "ColdTemp": 40, "HotTemp": 80,
                   "ColdColor": "#0040FF", "HotColor": "#FF0000", "TargetDevice": "", "TargetZone": "" },
  "Corrections": []
}
```

Para corrigir uma zona, adicione por exemplo `{ "Device": "ASRock", "Zone": "Addressable", "Order": "GRB" }` em `Corrections`. `Device` e `Zone` são trechos do nome que aparece no OpenRGB, e `Order` é a ordem em que os LEDs daquela zona leem as cores (`RGB`, `GRB`, `BRG`...). Para descobrir a ordem de uma peça nova: mande vermelho puro e veja que cor aparece.

## Compilar

Precisa do .NET 8 SDK no Windows:

```
dotnet build AbaddonRGB.sln -c Release
```

Estrutura:

- `src/AbaddonRGB.Core`: motores, composição das cores, perfis, automações, configurações e atualizador (sem interface).
- `src/AbaddonRGB`: a janela, o ícone da bandeja, a leitura de sensores e a detecção nativa (Windows Forms com controles desenhados à mão em `Controls.cs`).
- `src/AbaddonRGB/Assets`: logo, fundo, ícones e ilustração do gabinete (arte do projeto) e a fonte Oxanium (licença OFL, arquivo `Oxanium-OFL.txt`).

O GitHub Actions compila a cada push.

## Publicar uma versão nova (é o que dispara a atualização automática)

É automático: a cada push na `main`, o GitHub Actions compila, roda os testes e, se o `<Version>` de `src/AbaddonRGB/AbaddonRGB.csproj` ainda não tiver release, cria a release com o `AbaddonRGB.exe` e o `.sha256`. Os programas instalados se atualizam sozinhos em até 6 horas (ou na hora, em "Procurar atualização").

Para publicar, basta aumentar o `<Version>` (ex.: `0.4.6` → `0.4.7`). Commits que não mudam a versão não geram release. O botão **Run workflow** (aba **Actions** → **Build**) continua existindo para publicar de novo a versão atual.

Importante: o `.exe` precisa ficar numa pasta em que você tenha permissão de escrita (ex.: Documentos), não em `C:\Program Files`.

## Próximos passos

Veja o [ROADMAP](ROADMAP.md). A próxima etapa é a Fase 1: teclado e mouse Logitech sem o OpenRGB.
