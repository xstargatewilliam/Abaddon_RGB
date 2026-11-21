# Abaddon RGB

Programa para Windows que controla as luzes RGB do PC inteiro com uma cor só: placa-mãe, cooler, memórias, teclado e mouse.
Ele usa o [OpenRGB](https://openrgb.org) como "motor": o OpenRGB conversa com o hardware, e o Abaddon RGB manda as cores para ele pelo servidor SDK (porta 6742).

> **Estado: alfa.** Testado em um único PC (lista abaixo). Pode ter falhas e ainda não é um controlador universal.
> Se for testar, deixe o OpenRGB instalado e mande os registros (aba Sistema → Abrir pasta de registros).

## O que funciona hoje

| O quê | Como |
|---|---|
| Windows 10/11, 64 bits | Único sistema suportado (Linux e macOS não) |
| Placas-mãe, memórias, coolers e fitas RGB | Pelo OpenRGB, quando o OpenRGB reconhece a peça |
| Teclados Logitech G512, G610, G810, G910 e G Pro (com fio) | Motor nativo **experimental**, sem OpenRGB (aba Sistema). Só o G512 foi testado de verdade |
| Qualquer outro teclado, mouse ou controlador ARGB | **Não**, a menos que o OpenRGB reconheça |

Reconhecer uma peça não garante que todas as zonas e LEDs dela possam ser controlados: depende do que o próprio aparelho aceita.

**Proteções**
- Cada peça tem um limite de comandos por segundo. Peças desconhecidas ficam no limite mais seguro (4 por segundo).
- Somente leitura, "Não controlar esta peça" e peças desconectadas são conferidos logo antes de cada envio.
- Atualização com SHA-256; a versão anterior fica guardada até a nova provar que funciona, e volta sozinha se a nova cair duas vezes ao abrir.
- Configurações gravadas de forma atômica, com duas cópias de segurança.

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
4. Na página de **Releases** do repositório, baixe o **`AbaddonRGB-Setup.exe`** e abra. O Windows pede permissão (instala em `C:\Program Files\AbaddonRGB`, para todos os usuários) e cria o atalho no menu Iniciar. Não precisa instalar o .NET.
   - Se o Windows mostrar "O Windows protegeu o computador", clique em **Mais informações → Executar assim mesmo** (o instalador ainda não tem assinatura digital).
   - Prefere sem instalar? Baixe o `AbaddonRGB.exe` solto e abra de qualquer pasta.

**Atualizações.** Instalado em Program Files, o programa avisa quando sai versão nova; em **Sistema → Procurar atualização**, ele pede a permissão do Windows, baixa (conferindo o SHA-256) e abre de novo na versão nova. Se estiver aberto como administrador (inicialização no modo administrador), atualiza sem perguntar.

**Desinstalar.** Configurações do Windows → **Aplicativos → Aplicativos instalados** → Abaddon RGB → **Desinstalar**. Remove também a inicialização com o Windows e pergunta se você quer apagar as configurações (`%APPDATA%\AbaddonRGB`).

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

## Licença

GPL-3.0-or-later. O driver nativo dos teclados Logitech é um porte do OpenRGB (GPL-2.0-or-later); veja `NOTICE.md` para os créditos e as licenças das bibliotecas.
