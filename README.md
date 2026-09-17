# FTO - Sistema de Gestão e Controle de Vendas

Sistema desktop para controle financeiro, clientes, estoque, vendas e preparação de NF-e da empresa FTO. Interface com temas (claro/escuro), navegação unificada pós-login e **atualizações automáticas via GitHub Releases**.

Documentação de publicação e update: **[guia.md](guia.md)**

---

## Estrutura do projeto

```
FTO-Main/
├── guia.md                          # Guia de releases e atualização automática
├── README.md
├── .github/workflows/release.yml    # Build + ZIP na Release
└── FTO_Sistema/
    ├── FTO_App/
    │   ├── Services/
    │   │   ├── UpdateService.cs           # Verifica/baixa update do GitHub
    │   │   ├── EmpresaConfigStore.cs      # Config da empresa + credenciais da API Fiscal (criptografadas)
    │   │   ├── NfeXmlService.cs           # Gera XML NF-e local (visualização/backup, sem SEFAZ)
    │   │   ├── FiscalHomologacaoTextos.cs # Textos fixos exigidos pela SEFAZ em homologação (xNome/xProd)
    │   │   ├── ReformaTributariaService.cs# Cálculo de IBS/CBS por preset
    │   │   ├── FiscalPayloadBuilder.cs    # Monta o JSON de emissão (POST /emitir) para a API Fiscal
    │   │   ├── NfsePayloadBuilder.cs      # Monta o JSON DPS de emissão NFS-e (POST /nfse/emitir)
    │   │   ├── FiscalApiClient.cs         # HTTP client da API Fiscal (NF-e / NFS-e)
    │   │   ├── FiscalApiModels.cs         # DTOs de resposta da API Fiscal + FiscalApiResult<T>
    │   │   ├── NcmService.cs              # Autocomplete de NCM (BrasilAPI, sem chave)
    │   │   ├── DocumentoCadastroService.cs# Consulta CNPJ (Dados Abertos RF / MinhaReceita)
    │   │   ├── SecretProtector.cs         # Criptografia local (DPAPI) de API Key
    │   │   ├── ThermalPrinterService.cs   # Imprime o cupom não fiscal (Venda) na térmica
    │   │   ├── CupomPrintHelper.cs        # PrintVisual genérico p/ impressora térmica configurada
    │   │   ├── DanfsePdfService.cs        # Fallback local DANFSe (XML → QuestPDF NT 008)
    │   │   └── Danfse/                    # Parser XML + modelo + renderer DANFSe NT 008
    │   ├── Resources/
    │   │   └── nfse-logo-horizontal.png   # Logo oficial NFS-e (fallback local DANFSe)
    │   ├── views/
    │   │   ├── LoginView.*          # Login + atualizar sistema
    │   │   ├── MainShellView.*      # Shell com menu lateral
    │   │   ├── DashboardView.*      # Vendas
    │   │   ├── AnalyticsView.*      # Dashboard analítico
    │   │   ├── EstoqueView.*
    │   │   ├── ClientesView.*       # Cadastro fiscal completo + busca CNPJ
    │   │   ├── NotaFiscalView.*     # Cadastro NF-e (modelo 55) + produto do estoque + NCM
    │   │   ├── NotaFiscalAcoesWindow.*  # Emitir/consultar/XML/DANFE/CC-e/cancelar
    │   │   ├── NotaServicoView.*    # Cadastro NFS-e (DPS — campos obrigatórios)
    │   │   ├── NotaServicoAcoesWindow.* # Emitir/XML/DANFSE/cancelar NFS-e (SEFIN)
    │   │   ├── CancelamentoNfseWindow.* # Motivo 1/2/9 + justificativa (cancelamento NFS-e)
    │   │   ├── ProdutoEstoquePickerWindow.* # Seleção de produto com estoque para a NF
    │   │   ├── ReceiptCupomView.*       # Layout do cupom não fiscal (térmica 80mm)
    │   │   ├── ConfirmPrintWindow.*     # Confirmação de impressão do cupom (Vendas)
    │   │   ├── CancelamentoWindow.*     # Diálogo de cancelamento de NF-e
    │   │   ├── CartaCorrecaoWindow.*    # Diálogo de CC-e
    │   │   ├── InutilizacaoWindow.*     # Diálogo de inutilização de numeração
    │   │   ├── XmlViewerWindow.*        # Visualizador de XML autorizado
    │   │   └── ConfiguracoesView.*  # Empresa, fiscal (+logo emitente), API, cupom, dispositivos
    │   ├── models/
    │   │   ├── NotaServicoModel.cs  # Rascunho/emissão NFS-e (DPS)
    │   │   └── ...
    │   ├── Database.cs
    │   └── FTO_App.csproj
    └── FTO_App.Tests/                     # xUnit — DANFSe NT 008 (fixture XML + PDF)
```

---

## Navegação (após login)

Após autenticação, o sistema abre o **shell principal** com menu vertical à esquerda:

| Módulo | Função |
|--------|--------|
| **Vendas** | Lançamento em modal (padrão NF-e), cupom, filtros, estimativa IBS/CBS |
| **Dashboard** | Painel analítico (KPIs, top clientes) |
| **Estoque** | Produtos e categorias |
| **Clientes** | Cadastro completo (CPF/CNPJ, IE, endereço, IBGE, etc.) |
| **Nota Fiscal** | Cadastro (lançamento) de **NF-e (modelo 55)** com autocomplete de NCM; botão **⚡ Ações fiscais** abre emissão, status, XML/DANFE, cancelamento, CC-e e inutilização |
| **NFS-e** | Cadastro de DPS (tomador, serviço, ISS) e botão **Emitir NFS-e** para SEFIN Nacional via `Fiscal.NFSe.API` |
| **Configurações** | Empresa, fiscal (logo NF-e), **API Fiscal** (URLs NF-e e NFS-e), IBS/CBS, cupom, impressora/scanner |

O botão **Sair** retorna à tela de login.

---

## Funcionalidades

- **Controle de Acesso:** Login com usuário e senha.
- **Gestão de Vendas:** Lucro, filtros por data/cliente/status; tipo Serviço ou Venda de produto. Toolbar com quebra de linha (sem cortar botões). Clientes ficam no módulo próprio (botão removido de Vendas).
- **Clientes (módulo dedicado):** Cadastro fiscal com código IBGE e dados para NF-e. **Consulta de CNPJ** via Dados Abertos da Receita Federal (MinhaReceita, com fallback); sem busca automática de CPF.
- **Nota Fiscal:** Persistência de rascunhos, geração de XML local e **emissão real na SEFAZ via API Fiscal** (somente **NF-e modelo 55**). Botão **Produto do estoque** valida NCM/CFOP/preço/CST|CSOSN e preenche os campos do item. PIX/cartão enviam grupo `card` (`tpIntegra=2`) para evitar rejeição 391; cancelamento envia `dhEvento` local posterior à emissão (evita 577).
- **NFS-e:** Módulo próprio — cadastro da DPS e emissão/cancelamento via `Fiscal.NFSe.API`. Sem `nProt`; chave com 50 dígitos. **Baixar DANFSE** consome `GET /api/v1/nfse/danfe/{chave}` (com fallback local a partir do XML se a rota falhar).
- **Configurações:** Empresa, fiscal (logo do emitente), **API Fiscal** (NF-e / NFS-e), IBS/CBS, logo/cupom, banco e dispositivos.
- **Estoque e Analytics:** Produtos e painel financeiro.
- **Relatórios:** Excel (.xlsx) e PDF.
- **Impressão térmica:** Cupom **não fiscal** de vendas (título padrão **Comprovante de Vendas**), tipografia no estilo Imperial Colors (nome 13,5, dados 9, rótulos 10, total 12/14,5). Logo do emitente só na **DANFE NF-e** (PDF A4).
- **Atualização automática:** Botão na tela de login.

---

## Banco de dados (PostgreSQL)

O sistema usa **PostgreSQL** (via pgAdmin, sem Docker).

1. Instale o PostgreSQL e abra o **pgAdmin**
2. Crie o banco `fto`
3. Em `FTO_Sistema/FTO_App/.env` configure **apenas** a conexão:

```env
PGHOST=localhost
PGPORT=5432
PGDATABASE=fto
PGUSER=postgres
PGPASSWORD=sua_senha
```

Na primeira abertura, o app criptografa `PGPASSWORD` com DPAPI (`enc:...`) no mesmo usuário Windows.

4. Ao abrir o app, as tabelas são criadas automaticamente
5. Em **Configurações → Banco de dados**, clique em **Migrar dados do SQLite → PostgreSQL** para importar o `FTO.db` antigo (dados preservados; o arquivo SQLite não é apagado)

A migração também preenche **CPF/CNPJ** nas vendas a partir do cadastro de clientes quando o campo estava vazio.

**Valores monetários:** o SQLite legado guarda muitos valores como texto `160.00`. Se o dashboard mostrar totais ×100 (ex.: milhões), rode de novo **Migrar SQLite → PostgreSQL** — o parser foi corrigido (ponto = decimal, não milhar).

### Produtos — tributação

No estoque, o cadastro do produto tem a aba **Tributação e impostos** (NCM, CEST, CFOP, origem, CSOSN/CST, ICMS, PIS, COFINS + **IBS/CBS**). Os dados ficam na tabela `produtos`.

### Reforma tributária — IBS / CBS

Base legal: **EC 132/2023** e **LC 214/2025** (alíquota-teste arts. 343/346/348).

| Tributo | Papel | Teste 2026 | Projetado (cheio) |
|---------|-------|------------|-------------------|
| **CBS** | Federal (substitui PIS/COFINS) | 0,9% | ~9,21% |
| **IBS** | Estados + municípios (substitui ICMS/ISS) | **0,1% na UF** (`pIBSMun=0`) | ~18,7% |

Configurações → aba **IBS / CBS**:
- Presets: **Teste 2026**, **Projetado cheio** ou **Personalizado** (+ simulação em R$ 1.000)
- **Cálculo automático** na NF-e (checkbox no modal)
- Estimativa CBS/IBS/IVA dual no **modal de vendas** (usa alíquotas do produto quando cadastradas)
- Produto (estoque): CST, `cClassTrib`, alíquotas e % de redução
- XML local com `IBSCBS` (item: `gIBSUF`/`gIBSMun`/`vIBS`/`gCBS`) + `total.IBSCBSTot`

Em 2026 o **destaque** em DF-e é obrigatório no regime regular; o 1% é pedagógico (compensável com PIS/COFINS / dispensa de recolhimento se obrigações acessórias ok). Simples Nacional: destaque obrigatório a partir de **2027**.

### NF-e — corpo XML (autorização)

O gerador local (`NfeXmlService`) monta o corpo alinhado ao `GUIA_INTEGRACAO.md` da API Fiscal:

- **IBSCBS** no item com `gIBSUF` / `gIBSMun` / `vIBS` / `gCBS` (estrutura oficial)
- **`total.IBSCBSTot`** irmão de `ICMSTot`
- **ICMS:** `ICMS00`+CST se CRT=3; `ICMSSN`+CSOSN se Simples/MEI
- **Homologação:** `dest.xNome` fixo e `xProd` com `HOMOLOGACAO`
- UI: `idDest`, `indIEDest` (1/2/9), CSOSN, CEST, GTIN, consumidor final, presença

Ainda **não** assina nem transmite — isso fica na API (`POST /api/v1/nfe/emitir`).

Listas (Clientes, Vendas, Estoque, NF-e) usam **50 itens por página**, com a barra de paginação colada ao rodapé da grade.

---

## Configuração da empresa (banco)

Empresa, cupom, logo, ambiente, série e último número da NF-e ficam na tabela **`empresa_config`** (módulo **Configurações**). **Não** use `EMPRESA_*` / `NFE_*` / `CUPOM_*` no `.env`.

Se ainda existirem essas chaves no `.env` antigo, na primeira abertura o app importa para o banco e limpa o arquivo.

Use `.env.example` como modelo. **Nunca** faça commit do `.env`.

### Segurança

| Dado | Proteção |
|------|----------|
| `PGPASSWORD` no `.env` | DPAPI (`enc:…`) |
| API Key fiscal / integrações | DPAPI no PostgreSQL |
| Senhas de usuários | PBKDF2 (upgrade automático no login) |

Opcional para repositório **privado**:

```env
FTO_UPDATE_TOKEN=ghp_seu_token
```

---

## Gerar executável (local)

Na raiz do repositório:

```powershell
dotnet publish "FTO_Sistema\FTO_App\FTO_App.csproj" -p:PublishProfile=Win64-SelfContained
```

Saída: `FTO_Sistema\publish\FTO_App-win-x64\FTO_App.exe`

**Entrega manual:** compacte a pasta inteira em `.zip`. O cliente deve extrair e rodar `FTO_App.exe` (não só o `.exe`).

---

## Atualização automática

1. Na tela de login, clique em **Atualizar sistema**.
2. Se houver Release mais nova no GitHub, o app baixa `FTO_App-win-x64.zip`, aplica e reinicia.
3. **Preservados:** `.env`, `FTO.db` (e arquivos WAL).

### Publicar update (resumo)

1. `git push` das alterações  
2. Criar Release com tag `vX.Y.Z` (maior que a versão atual)  
3. Aguardar Action **Build Release Package** anexar o ZIP  
4. Cliente usa o botão na login  

Passo a passo completo: **[guia.md](guia.md)**

---

## Tecnologias

| Item | Tecnologia |
|------|------------|
| UI | C# / WPF (.NET 8) |
| Banco | SQLite (WAL) |
| Excel | ClosedXML |
| PDF | QuestPDF |
| Update | GitHub Releases API |

---

## Requisitos

- Windows 10/11 64-bits  
- Build self-contained: cliente **não** precisa instalar .NET  
- Conexão com internet para verificar/atualizar  

---

## Como instalar (primeira vez)

1. Baixe `FTO_App-win-x64.zip` da [Release mais recente](https://github.com/kapimkk/FTO-Main/releases/latest)  
2. Extraia (ex. `C:\FTO Sistema`)  
3. Configure o `.env` (ou use Configurações após o login)  
4. Execute `FTO_App.exe`

---

## Integração com a API Fiscal (emissão real de NF-e / NFS-e)

O módulo **Nota Fiscal** usa `Fiscal.NFe.API` (**modelo 55**). O módulo **NFS-e** usa `Fiscal.NFSe.API` (Padrão Nacional / SEFIN). **NFC-e (modelo 65) foi removida do FTO.** Autenticação por `X-API-Key`.

### Configuração (Configurações → Fiscal / NF-e)

| Campo | Descrição |
|---|---|
| URL base — NF-e | Endereço do `Fiscal.NFe.API` (ex.: `http://localhost:5001`) |
| URL base — NFS-e | Endereço do `Fiscal.NFSe.API` (ex.: `http://localhost:5003`) |
| Série / Último nº NFS-e | Numeração da DPS (sugerida no cadastro) |
| API Key | Chave `pfcode_...` emitida no Portal Administrativo. Fica **criptografada com DPAPI** no banco |
| cTribNac padrão + **Fixar** | Código de 6 dígitos da lista nacional sugerido em toda NFS-e (ex.: `010701` — serviços de TI). Com **Fixar** marcado, o campo fica somente leitura no cadastro e toda nota sai com esse código |
| pTotTribFed / Est / Mun % | Totais aproximados Lei 12.741 (DPS com `opSimpNac=1`). Mun vazio = alíquota ISS da nota |
| Enviar pAliq | Opcional — força `pAliq` (padrão: omite conforme E0617/E0625) |
| Enviar endereço prestador | Opcional — padrão omitido (E0128 com `tpEmit=1`) |
| Testar conexão | Chama `GET /health` dos serviços NF-e e NFS-e |

Prestador da NFS-e (CNPJ, IM, e-mail, telefone, endereço) vem da aba **Empresa**. `xNome` do prestador **não** é enviado (`tpEmit=1` / E0121 — regra SEFIN fixa).

O CNPJ da empresa (aba **Empresa**) precisa ser o **mesmo CNPJ cadastrado como Tenant** dessa API Key no Portal; caso contrário toda emissão retorna 401/403.

### Cadastro (lançamento) × Ações fiscais — duas telas com responsabilidade única

A tela **Nota Fiscal** foi separada em duas responsabilidades:

- **Cadastro** (`NotaFiscalView`): lançamento de **NF-e** — botão **+ Nova NF-e**. Rodapé do formulário: **Excluir / Cancelar / Salvar**.
- **Ações fiscais** (`NotaFiscalAcoesWindow`): aberta por **⚡ Ações fiscais** na lista; concentra a integração com a API Fiscal:

| Ação | Endpoint chamado | Observação |
|---|---|---|
| 📄 Gerar XML (rascunho) | *local* | Grava o XML de referência em disco e marca a nota como "XML gerado" |
| 🚀 Emitir na SEFAZ | `POST /api/v1/nfe/emitir` | Monta o JSON com a data/hora **atual** (`dhEmi`) e mostra `cStat`/`xMotivo`/chave/protocolo |
| 📊 Consultar status | `GET /api/v1/notas/status/{chave}` | Situação normalizada (Autorizada/Cancelada/Denegada/Rejeitada/Inexistente) |
| ⬇️ Baixar XML | `GET /api/v1/notas/xml/{chave}` | Salva o `nfeProc` autorizado em disco |
| 👁️ Ver XML | idem | Abre visualizador com formatação, cópia e exportação |
| 🖨️ Baixar DANFE | `GET /api/v1/notas/danfe/{chave}` | PDF A4 (com opção de DANFE local com logo do emitente) |
| ✍️ Carta de Correção | `POST /api/v1/nfe/carta-correcao` | Bloqueia localmente textos que tentem corrigir valor/imposto/destinatário/preço |
| 🛑 Cancelar nota | `POST /api/v1/nfe/cancelar` | Exige protocolo de autorização e justificativa (≥ 15 caracteres) |
| 🚫 Inutilizar numeração | `POST /api/v1/nfe/inutilizar` | Na barra da **lista** (faixa nunca emitida) |

O **Ambiente** (Produção/Homologação) da NF-e não é mais escolhido por nota: segue sempre **Configurações → Fiscal / NF-e**, tanto no cadastro quanto na janela de Ações fiscais (campo mostrado, mas travado — só muda alterando lá). Uma nota já **Emitida** ou **Cancelada** preserva o ambiente real em que foi transmitida, mesmo que a configuração mude depois. A emissão usa a data/hora **do instante do clique**.

Toda chamada retorna um `FiscalApiResult<T>` padronizado.

### NFS-e — Padrão Nacional (SEFIN)

Módulo **NFS-e** no menu lateral (`NotaServicoView` + `NotaServicoAcoesWindow`):

| Ação | Endpoint / origem | Observação |
|---|---|---|
| 🚀 Emitir NFS-e | `POST /api/v1/nfse/emitir` | Payload DPS (`NfsePayloadBuilder`); `tpEmit=1` sem `xNome` do prestador; IM só se cadastrada |
| ⬇️ / 👁️ XML | `GET /api/v1/notas/xml/{chave}` | Chave com **50** dígitos; sem `nProt` |
| 🖨️ Baixar DANFSE | `GET /api/v1/nfse/danfe/{chave}` | Download do PDF pela API Fiscal. Se a rota falhar, fallback local (`DanfsePdfService.GerarDeXml` / NT 008) |
| 🛑 Cancelar | `POST /api/v1/nfse/cancelar` | `codigoMotivo` 1/2/9 + justificativa 15–255 |

O gerador local (parser + QuestPDF) permanece como **fallback** e para testes (`FTO_App.Tests`). QR de consulta: `https://www.nfse.gov.br/ConsultaPublica/?tpc=1&chave={50digitos}`.

Referência NT: [NT 008 SE/CGNFSe DANFSe v1.02](https://www.gov.br/nfse/pt-br/biblioteca/documentacao-tecnica/rtc/nt-008-se-cgnfse-danfse-20260714-v1-02.pdf).

Campos obrigatórios no cadastro: série, nº DPS, competência, IBGE emissão/prestação, tomador (CPF/CNPJ + nome), `cTribNac` (6 dígitos), descrição, valor, ISS (`tribISSQN` / `tpRetISSQN` / alíquota). IBS/CBS opcional exige NBS de 9 dígitos.

⚠️ **Ambiente (`tpAmb`) e URL SEFIN:**
- Homologação (`tpAmb=2`) → `https://sefin.producaorestrita.nfse.gov.br/API/SefinNacional`
- Produção (`tpAmb=1`) → `https://sefin.nfse.gov.br/SefinNacional` (**sem** `/API` — com `/API` o IIS responde 404 HTML)
- Com `AmbienteRestrito=false` na API, o host é escolhido pelo `tpAmb` de cada emissão. Com `true`, só sandbox.

### Comunicação HTTP (homologação e produção)

O cliente (`FiscalApiClient`) foi endurecido para não interromper a ida até a API/SEFAZ quando as duas pontas já estão configuradas:

- **Timeout 180s** — autorização SEFAZ via API costuma demorar mais que um minuto.
- **TLS 1.2/1.3**, descompressão automática, sem cookies e sem `Expect: 100-continue`.
- **`tpAmb` normalizado** (`1`=produção / `2`=homologação) em emissão, cancelamento, CC-e, inutilização e consultas.
- **1 retentativa** em HTTP 502/503/504 ou `SEFAZ_UNAVAILABLE`.
- O ambiente escolhido na janela **Ações fiscais** é gravado na nota antes do POST.

> O erro `[HTTP 502] SEFAZ_UNAVAILABLE` significa que o FTO_App **já falou com a API Fiscal**; quem falhou foi a API ao contatar o webservice da SEFAZ. Nesse caso não há ajuste de payload no app — só esperar a SEFAZ ou conferir certificado/endpoint no lado da API.

### Correções de rejeição na SEFAZ (`dhEmi`, `xProd`, NCM, `cClassTrib`, IBS UF)

Rejeições reais corrigidas na raiz — tanto no JSON (`FiscalPayloadBuilder`, o que de fato é enviado à API) quanto no XML local (`NfeXmlService`):

- **Data/hora de emissão desatualizada** (tolerância de poucos minutos): `dhEmi` usa `DateTime.Now` **no instante do clique** em "Gerar XML"/"Emitir na SEFAZ".
- **`xProd` sem o texto de homologação exigido**: em Homologação (`tpAmb=2`), a descrição do item é sempre o texto exato da SEFAZ (Rejeição 373) — centralizado em `Services/FiscalHomologacaoTextos.cs`.
- **NCM vazio (`XSD_VALIDATION`)**: o elemento `NCM` não pode ser `''`. O cadastro normaliza para só dígitos; a emissão **bloqueia** se o NCM não tiver 2 ou 8 dígitos; ao escolher sugestão da BrasilAPI, pontos são removidos.
- **`cClassTrib = '0'` (`XSD_VALIDATION` / `TcClassTrib`)**: o schema exige **6 dígitos**. Valores inválidos (ex.: `"0"`) são normalizados para `000001` antes do POST.
- **Rejeição 1026 — Alíquota do IBS da UF inválida**: em 2025/2026 a SEFAZ exige `pIBSUF = 0,1%` (NT 2025.002 / art. 343 LC 214/2025). O app enviava `0,05%` (rateio errado UF/Mun). Agora `CalcularParaEmissao` força `pIBSUF=0,1` e `pIBSMun=0` no payload de emissão, independentemente do valor salvo no rascunho.
- **HTTP 502 / `SEFAZ_UNAVAILABLE`**: falha de comunicação com o webservice da SEFAZ (ex.: homologação PR). Não é erro de payload — verificar [disponibilidade](https://www.nfe.fazenda.gov.br/portal/disponibilidade.aspx) e tentar de novo.

### Autocomplete de NCM

No cadastro, o campo **NCM** consulta a **BrasilAPI** (`GET /api/ncm/v1?search=...`, sem chave) conforme o usuário digita — mínimo 3 caracteres, com debounce de ~350ms para não disparar uma requisição por tecla. As sugestões (`código — descrição`) aparecem em um popup abaixo do campo; ao clicar em uma, só o código é preenchido. Falha de rede não bloqueia o cadastro — o usuário sempre pode digitar o NCM manualmente, exatamente como já acontece com o CEP (ViaCEP).

### Contrato do payload (`FiscalPayloadBuilder`)

O JSON de emissão foi construído e conferido **campo a campo contra o código-fonte real da API** (`Fiscal.Shared.Models.NFeModels`/`ImpostoModels`/`TotalModels` e `JsonToXsdResolverService`), não apenas contra a documentação:

- `ide`, `emit`, `dest`, `det[].prod`, `det[].imposto` (ICMS/PIS/COFINS) e `total.ICMSTot` seguem a nomenclatura oficial do layout NF-e 4.00.
- Grupos "choice" do XSD (ICMS/PIS/COFINS/IBSCBS) são enviados como wrapper `*Details` (`icmsDetails`, `pisDetails`, `cofinsDetails`, `tribDetails`) com os campos **linearizados** — é assim que `JsonToXsdResolverService` da API resolve o tipo concreto (`TICMS00`, `TICMSSN102`, `TCibsNFe`, etc.), a partir do `CST`/`CSOSN` informado dentro do wrapper.
- `total.IBSCBSTot` é montado como **grupo irmão** de `ICMSTot` (nunca dentro dele — o XSD 2026 rejeita `vIBS`/`vCBS` dentro de `ICMSTot`).
- `cNF` (código numérico) e `cDV` (dígito verificador) são deixados em branco de propósito: a própria API os calcula ao montar a chave de acesso.
- Responsável Técnico (`infRespTec`) também não é enviado pelo app — a API preenche automaticamente a partir do cadastro do Tenant no Portal.

### Testando em homologação

1. No Portal Administrativo da API, cadastre o Tenant (mesmo CNPJ da empresa no FTO_App), envie o certificado A1 e gere a API Key.
2. Em **Configurações → Fiscal / NF-e**, informe as URLs dos microsserviços e a API Key; use **Testar conexão**.
3. Em **Nota Fiscal**, deixe **Ambiente = 2-Homologação** (padrão) e clique **🚀 Emitir na SEFAZ** — o destinatário e o nome do produto são automaticamente ajustados para o texto exigido em homologação.
4. Use os botões de status/XML/DANFE para conferir o resultado oficial devolvido pela SEFAZ.

> A subida da infraestrutura da API (Docker/Postgres/Portal) e o certificado digital A1 são de responsabilidade de quem opera a API Fiscal — o FTO_App já está pronto para apontar para qualquer instância (local, homologação ou produção) só trocando a URL e a API Key.

---

## Novidades recentes

- **Correção: "Atualizar sistema" dizia sucesso mas não atualizava nada.** O script que troca os arquivos depois que o app fecha era gerado com erro de sintaxe (`@('.env' 'FTO.db')` — sem vírgula, introduzido junto com o `robocopy`). O PowerShell recusa o arquivo **inteiro** quando não consegue interpretá-lo, então nada era copiado e ninguém reabria o app: download concluía, o app fechava e voltava na versão antiga. Além da vírgula, o que tornava isso invisível também foi corrigido — o script agora **grava log** (`%TEMP%\FTO_Update\ultima-atualizacao.log`), deixa o resultado num arquivo que o app **mostra na abertura seguinte** (atualizado com sucesso / não foi aplicado + motivo) e **sempre reabre o app**, dando certo ou não. O `.ps1` passou a ser gravado em UTF-8 **com BOM** (sem BOM, o PowerShell 5.1 lê como ANSI e bytes do UTF-8 viram aspas tipográficas, que ele trata como delimitador de string). As sobras das tentativas anteriores no TEMP (~270 MB cada) são apagadas antes de cada download. Quatro testes novos **executam o script de verdade**: sintaxe, cópia preservando `.env`/`FTO.db`, caminho de falha e leitura única do resultado.

- **NF-e com vários itens:** a nota deixou de ser "um produto por nota". O cadastro ganhou a etapa **Itens da nota** com grade (código, descrição, NCM, CFOP, quantidade, unitário, total) e ações por linha (editar, duplicar, remover; duplo clique edita, Delete remove). O editor de item abre com **Enter = “Salvar e adicionar outro”**: o item entra na nota na hora e o formulário volta limpo herdando CFOP, unidade e tributação do anterior — dá para lançar vários produtos seguidos sem reabrir nada. **📦 Do estoque** escolhe o produto e já abre o item preenchido, com o cursor na quantidade.
  Na emissão (JSON da API e XML local) cada item vira um `det` com `nItem` sequencial, e os totais são a **soma exata dos valores já arredondados de cada item** — é assim que a SEFAZ confere (rejeições 531/533/564). Regras que passaram a valer por item: CST 40/41/50 não entra na base do ICMS (o XML agora sai como `ICMS40`), PIS/COFINS com CST não tributado não soma no total, e em homologação **só o 1º item** recebe o `xProd` padrão (rejeição 373). Os itens ficam na coluna `itensjson`; nota antiga, sem a coluna preenchida, é convertida automaticamente em um item a partir das colunas `produto*` — e ao salvar o 1º item continua espelhado nelas, para uma estação ainda na versão anterior não abrir a nota vazia. O DANFE local com logo passou a listar todos os itens em tabela.
- **Cadastro de NF-e e NFS-e redesenhado:** etapas numeradas em cartões (Identificação, Destinatário/Tomador, Itens/Serviço, Pagamento…), campos alinhados em grade com rótulo e dica, selo **HOMOLOGAÇÃO/PRODUÇÃO** no cabeçalho, busca de cliente digitando no combo e rodapé fixo com os totais (produtos, ICMS, PIS/COFINS, IBS/CBS e total da nota; na NFS-e, o valor do serviço). Fechar uma NF-e nova com itens lançados pede confirmação.

- **Backup completo no servidor:** **Configurações → Backup → 🗄️ Realizar backup do sistema**. Gera o dump do PostgreSQL com `pg_dump` (formato *custom*, restaurável com `pg_restore`) e envia junto o `.env`, o `devices.json`, os XMLs de NF-e, o certificado digital e o logo do emitente. As pastas são criadas por mês e dia no destino: `Setembro-2026\09-09-2026\143025\` (a hora evita sobrescrever backups do mesmo dia). O pacote é montado numa pasta temporária local e só depois copiado — `pg_dump` escrevendo direto num compartilhamento é lento e, se a rede cair no meio, deixa um `.dump` truncado com cara de backup válido. O `RESUMO.txt` é gravado por último e lista o que entrou, o que **não** entrou e como restaurar: pasta sem `RESUMO.txt` é backup que não terminou. Configuração no `.env` (`BACKUP_DESTINO`, `BACKUP_USUARIO`, `BACKUP_SENHA`, `BACKUP_PG_DUMP`) — veja `.env.example`; a senha do servidor é criptografada com DPAPI na primeira leitura, igual ao `PGPASSWORD`.

- **Atualização do sistema mais rápida:** o pacote passou a ser publicado como **arquivo único comprimido** — 25 arquivos / 86 MB no lugar de 304 / 192 MB, e o ZIP caiu de 80,3 para 74,4 MB. O ganho maior é no cliente: extrair 304 arquivos e copiá-los um a um (com o antivírus abrindo cada um) era o que dominava os minutos de espera; agora a cópia é um `robocopy` único. Saíram também o Entity Framework 6 (vinha junto do pacote `System.Data.SQLite`, e o app nunca usou EF) e a dependência direta de `System.Drawing.Common` — a lista de impressoras passou a usar `System.Printing`, o mesmo do caminho de impressão, então o que a tela mostra é exatamente o que a impressão consegue abrir. O download agora mostra **porcentagem, MB e velocidade** em vez de um "Baixando pacote..." parado, e confere o tamanho recebido contra o da release: download cortado vira uma mensagem de conexão, não um erro de ZIP corrompido.

- **Cupom não fiscal — tipografia:** o cupom foi reescalado para a mesma **densidade** do Imperial Colors. Copiar os tamanhos em DIP não bastava: lá o cartão tem ~416 DIP de largura e aqui 302 (bobina de 80 mm real), então a mesma fonte ocupava proporcionalmente muito mais linha. Escala final ~0,85 do original (nome 16→13,5, dados 10→9, rótulos/valores 12→10, itens 11→10, total 15/18→12/14,5, pagamento/título 11→9,5, rodapé 12→10), espaçamentos e padding proporcionais. O cupom ficou **~16% mais curto** (514→433 DIP de altura), o que também economiza bobina. Vale para a pré-visualização, a térmica e o PDF (7,5pt de corpo = os 10 DIP da tela).
- **Cupom — bloco da empresa recentrado:** o cabeçalho com os dados da empresa é o único bloco centralizado do cupom (o resto é alinhado à esquerda), então a margem física que o driver reserva na bobina aparecia nele como um desvio para a direita. Ele passa a ter uma margem negativa de 10 DIP à esquerda, recentrando-o sobre o papel.
- **Impressão térmica em rede (cupom falhado):** com o papel já correto, o cupom ainda saía esgarçado/apagado na fila de rede. Cor, qualidade e resolução são padrões **por fila**: em *Color* o driver converte o cinza do antialiasing/ClearType em retícula e o texto sai pontilhado; em *Draft* imprime com menos pontos e sai apagado. Agora o PrintTicket força **monocromático, qualidade não-rascunho e a maior resolução declarada** — a fila de rede passa a imprimir igual à USB. Também saiu do caminho de impressão o `VisualBrush` (rasterizava o cupom a 96 DPI para reamostrar nos 203 DPI da térmica): a margem física do driver virou *padding* e a redução de segurança virou `LayoutTransform`, mantendo tudo vetorial. O texto do cupom passa a ser impresso em `Ideal`/`Grayscale` (era `Display`/`ClearType`, que é modo de tela) e as linhas separadoras saem pretas cheias em vez de 15% de preto. O **🔎 Diagnóstico da impressora** agora mostra cor/qualidade/resolução da fila e o que o app força.
- **Impressão térmica em rede (cupom cortado):** o cupom era medido em **80 mm** (largura do *papel*) quando a fila não reportava um tamanho de bobina — mas uma térmica de 80 mm só imprime **~72 mm** por linha, então a borda direita saía cortada. Via USB o driver reporta a área imprimível correta (73,6 mm na MP-2500 TH) e por isso funcionava; numa fila de rede em A4 caía no fallback errado. Agora o app usa a área **imprimível** (nunca a do papel), seleciona a mídia de bobina quando o driver oferece, respeita a margem física do driver e encolhe proporcionalmente como rede de segurança. Em **Configurações → Dispositivos** há um botão **🔎 Diagnóstico da impressora** que mostra papel, área imprimível e tamanhos do driver — use para comparar a fila USB com a de rede.
- **Ambiente NF-e fixo pela configuração:** o combo de Produção/Homologação no cadastro e na janela de Ações fiscais deixou de ser editável — segue sempre Configurações → Fiscal / NF-e. Nota já Emitida/Cancelada mantém o ambiente real da emissão.
- **cTribNac configurável:** código padrão da NFS-e em Configurações → Fiscal, com opção **Fixar** (bloqueia a edição no cadastro).
- **Nome dos PDFs baixados:** DANFSe e DANFE saem como `NotaFiscalServico-NomeTomador-ddMMyyyy.pdf` / `NotaFiscal-NomeDestinatario-ddMMyyyy.pdf` (sem acento, sem caractere inválido).
- **Painel analítico:** "Valor em aberto" passa a somar o **valor de venda** dos lançamentos com status *Em Aberto* — antes somava lucro e incluía *Em execução* e *Não aprovado*. Gráfico mensal em escala linear (era logarítmica).
- **"Não aprovado" fora do faturamento:** orçamento recusado continua na lista de Vendas, mas não entra em total de vendas, gastos, lucro, margem, ticket médio, gráficos nem no Top 5 clientes. O painel e o rodapé da grade mostram quantos ficaram de fora.
- **Alíquotas 2027/2028:** a partir de 2027 o IBS vai a 0,05% estadual + 0,05% municipal e a CBS sai da alíquota-teste (LC 214/2025 art. 346). A CBS cheia vem de Configurações → IBS / CBS; enquanto não for confirmada, a tela avisa e a emissão usa a referência projetada.
- **Datas fiscais tipadas:** `notasfiscais.dataemissao` virou `TIMESTAMP` e `notasservico.datacompetencia` virou `DATE` (eram texto). Conversão automática e tolerante — valor fora do padrão ISO vira nulo em vez de travar a abertura.
- **Integridade referencial:** FKs `vendas.produtoid → produtos.id` e `notasfiscais.clienteid → clientes.id` com `ON DELETE SET NULL`; órfãos existentes são zerados antes de criar a constraint.
- **Busca case-insensitive:** Vendas, Clientes, Estoque e NF-e usam `ILIKE` (no PostgreSQL o `LIKE` é sensível a maiúsculas — no SQLite legado não era).
- **Banco:** valores da `notasfiscais` migrados de `DOUBLE PRECISION` para `NUMERIC`; índices de apoio (paginação de vendas, join por nome, chave de acesso, status); DDL de startup em lote; venda + baixa de estoque na mesma transação.
- **Numeração NF-e por série e ambiente:** emissão em homologação não consome mais número da produção.
- **Remoção da NFC-e:** o FTO emite apenas **NF-e (55)** e **NFS-e**. Removidos modelo 65, CSC, URL `Fiscal.NFCe.API`, DANFE térmica NFC-e e QR Code de consumidor.
- **NFS-e — Configurações:** `pTotTribFed/Est/Mun` (Lei 12.741), opções para forçar `pAliq` ou endereço do prestador; CNPJ/IM do prestador na aba Empresa.
- **NFS-e — DANFSE via API:** **Baixar DANFSE** chama `GET /api/v1/nfse/danfe/{chave}` e salva o PDF; fallback local (XML + NT 008) se a rota falhar.
- **NFS-e — auditoria DB/código:** UPDATE não zera `status`; bloqueio edição/exclusão Emitida/Cancelada; nº DPS por série; IBGE na emissão.
- **NFS-e — CEP / rejeições SEFIN:** ViaCEP no tomador; E0166/E0625/E0617/E0128 tratados no `NfsePayloadBuilder`.
- **Correções NF-e:** `vProd`, IE×indIEDest, NCM, `cClassTrib`, `pIBSUF=0,1%`, `dhEmi` no clique, homologação `xProd`.
- **Cupom não fiscal (vendas):** impressão térmica do comprovante de vendas mantida (independente de NFC-e).
- **PostgreSQL / update automático / clientes / estoque:** conforme releases anteriores.