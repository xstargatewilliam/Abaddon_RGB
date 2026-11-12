# Roteiro: Abaddon RGB funcionando sozinho

Objetivo: o Abaddon RGB falar direto com o hardware, sem depender do OpenRGB.
A troca é gradual: cada peça passa para o motor nativo quando o driver dela estiver pronto e testado.
Enquanto isso, ela continua pelo motor OpenRGB.

| Fase | O que entrega | Situação |
|---|---|---|
| 0 | Camada de motores, modo somente leitura, registro, interface em abas, perfis, automações | **Feita (v0.3)** |
| 1 | Driver nativo do teclado G512 e do mouse G305 (USB, protocolo HID++) | A fazer |
| 2 | Driver nativo da placa ASRock B450M + cooler (SMBus via PawnIO, protocolo Polychrome) | A fazer |
| 4 | Serviço do Windows com permissão de administrador + instalador | A fazer |
| 3 | Driver nativo das memórias (chip ENE, SMBus) — a fase mais delicada, por isso depois da 4 | A fazer |
| 5 | Acabamento: convivência com monitores de hardware, remoção do motor OpenRGB | A fazer |

## Fase 0 em detalhe (v0.3)

- `ILightingEngine`: contrato que todo motor cumpre (conectar, listar peças, enviar cores).
- `OpenRgbEngine`: o motor atual. `NativeEngine` + `INativeDriver`: onde os drivers próprios vão entrar.
- `LightingController`: decide a cor de cada LED (cor geral → cor própria da peça → cor da zona → temperatura → brilho → correção de ordem).
- Segurança: modo somente leitura, "não controlar esta peça", registro em `%APPDATA%\AbaddonRGB\logs`.
- Detecção nativa (teste): lista os dispositivos USB conhecidos sem abrir nem escrever em nenhum.

## Regras para os drivers nativos

1. **Código próprio.** Os protocolos são estudados (documentação pública, captura de tráfego USB, comportamento observado) e reescritos do zero. Nada de copiar trechos do OpenRGB: ele é GPL e este projeto é MIT.
2. **Ler antes de escrever.** Todo driver primeiro identifica a peça e só escreve depois de confirmar que é ela.
3. **SMBus com lista fixa de endereços.** Nunca varrer o barramento escrevendo: os chips de configuração das memórias (SPD) ficam nele.
4. **Sempre com volta.** Qualquer peça pode ser devolvida ao motor OpenRGB ou desligada do programa pela interface.
