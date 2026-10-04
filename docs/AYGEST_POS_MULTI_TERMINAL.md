# AyGest POS — Arquitectura Multi-Terminal

Esta versão prepara o AyGest POS para trabalhar com vários computadores usando uma arquitetura **Servidor + Terminais**.

## Modos

- **Local** — o computador trabalha com a base local.
- **Servidor** — um computador mantém a base de dados local e disponibiliza a API do AyGest na rede local.
- **Cliente** — os restantes computadores ligam-se ao servidor através da API.

## Identidade de terminal

Cada instalação recebe automaticamente um `TerminalId` persistente e um `TerminalName`. As chamadas feitas pelo cliente incluem:

- `X-Terminal-Id`
- `X-Terminal-Name`

Isto permite distinguir vendas, operações e auditoria por caixa/computador sem depender do endereço IP.

## Configuração

O ficheiro de configuração fica em:

`%APPDATA%\AyGestRest\config.json`

Campos principais:

- `Mode`
- `ServerIp`
- `ServerPort`
- `DiscoveryPort`
- `TerminalId`
- `TerminalName`
- `ApiBaseUrl`
- `AutoDiscoverServer`
- `OfflineQueueEnabled`
- `CurrencyCode` = `MZN`
- `CurrencySymbol` = `MT`
- `InvoiceSeries`

## Funcionamento recomendado

Para uma loja com vários computadores:

1. Instalar o AyGest POS num computador que ficará como servidor.
2. Selecionar o modo **Servidor**.
3. Garantir que a porta configurada da API está acessível na rede local.
4. Instalar o AyGest POS nos restantes computadores.
5. Selecionar **Cliente** e indicar o IP/nome do servidor, ou usar a descoberta automática.
6. Cada terminal mantém o seu próprio identificador, enquanto as operações centralizadas passam pelo servidor.

## Faturação

Foi adicionada a camada de faturação com:

- numeração por série e ano;
- moeda MZN;
- cliente e NUIT;
- subtotal, desconto, IVA, total, valor recebido e troco;
- método de pagamento;
- identificação do terminal emissor;
- anulação com motivo e data;
- itens de fatura ligados ao produto;
- criação automática das tabelas SQLite necessárias.

O formato inicial de numeração é:

`A-2026/000001`

A série e os dados do emitente são configuráveis e não ficam hardcoded como dados de negócio.

## Base funcional do POS

A implementação preserva a base existente do AyGest Rest e segue as ideias que tornam o BMS Point of Sale uma boa referência: POS rápido, inventário, funcionários, caixa, devoluções, relatórios, auditoria, impressão e terminais identificados. O objetivo é transformar o AyGest numa solução própria, em vez de manter uma cópia visual ou funcional do projeto externo.
