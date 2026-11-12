# Abaddon RGB

Programa para Windows que controla as luzes RGB do PC inteiro com uma cor só: placa-mãe, cooler, memórias, teclado e mouse.
Ele usa o [OpenRGB](https://openrgb.org) como "motor": o OpenRGB conversa com o hardware, e o Abaddon RGB manda as cores para ele pelo servidor SDK (porta 6742).

## O que a versão 0.1 faz

- Aplica uma cor em todos os dispositivos de uma vez (seletor de cor, hexadecimal ou cores rápidas).
- Controla o brilho (0 a 100%).
- **Corrige a ordem das cores por zona.** O cooler Rise Mode ligado no conector ARGB da ASRock B450M Steel Legend lê as cores como G-R-B (vermelho e verde trocados). O programa troca os canais sozinho só nessa zona.
- Fica na bandeja do Windows (perto do relógio). O ícone mostra a cor atual.
- Inicia com o Windows (opcional), escondido, e reaplica a última cor assim que o OpenRGB estiver disponível. Isso resolve as memórias que não guardam a cor sozinhas.

## Hardware testado

| Peça | Como aparece no OpenRGB | Observação |
|---|---|---|
| ASRock B450M Steel Legend | ASRock B450M Steel Legend | Controlada pelo SMBus (precisa do PawnIO) |
| Water cooler Rise Mode (ARGB) | zona "Addressable Header" da placa | Ordem GRB; 1 cor por vez |
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
  "Corrections": [
    { "Device": "ASRock", "Zone": "Addressable", "Order": "GRB" }
  ]
}
```

Em `Corrections`, `Device` e `Zone` são trechos do nome que aparece no OpenRGB, e `Order` é a ordem em que os LEDs daquela zona leem as cores (`RGB`, `GRB`, `BRG`...). Para descobrir a ordem de uma peça nova: mande vermelho puro e veja que cor aparece.

## Compilar

Precisa do .NET 8 SDK no Windows:

```
dotnet build AbaddonRGB.sln -c Release
```

Estrutura:

- `src/AbaddonRGB.Core`: conexão com o OpenRGB, correção de cores e configurações (sem interface).
- `src/AbaddonRGB`: a janela e o ícone da bandeja (Windows Forms).

O GitHub Actions compila a cada push. Criar uma tag `v*` gera uma release com o `.exe`.

## Próximas ideias

- Cor diferente por dispositivo/zona.
- Efeitos animados (respiração, onda passando por todas as peças).
- Perfis (trabalho, jogo, noite).
- Reagir a música, uso de CPU ou temperatura.
