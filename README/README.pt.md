<h1 align="center">
  SPAN Finder
</h1>

<p align="center">
  <strong>As Miller Columns do macOS Finder, agora no Windows.</strong><br>
  Para quem migrou para o Windows, mas nunca esqueceu a navegacao em colunas do Finder.
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://img.shields.io/badge/Microsoft_Store-Download-blue?style=for-the-badge&logo=microsoft" alt="Microsoft Store"></a>
  <a href="https://github.com/LumiBearStudio/SpanFinder/releases/latest"><img src="https://img.shields.io/github/v/release/LumiBearStudio/SpanFinder?style=for-the-badge&label=Latest" alt="Latest Release"></a>
  <a href="../LICENSE"><img src="https://img.shields.io/github/license/LumiBearStudio/SpanFinder?style=for-the-badge" alt="License"></a>
  <a href="https://github.com/sponsors/LumiBearStudio"><img src="https://img.shields.io/badge/Sponsor-%E2%9D%A4-ff69b4?style=for-the-badge&logo=github-sponsors" alt="Sponsor"></a>
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://get.microsoft.com/images/pt-br%20dark.svg" width="200" alt="Baixar na Microsoft Store"></a>
</p>

<p align="center">
  <a href="../README.md">English</a> | <a href="README.ko.md">한국어</a> | <a href="README.ja.md">日本語</a> | <a href="README.zh-CN.md">中文(简体)</a> | <a href="README.zh-TW.md">中文(繁體)</a> | <a href="README.de.md">Deutsch</a> | <a href="README.es.md">Español</a> | <a href="README.fr.md">Français</a> | Português
</p>

---

![SPAN Finder — Navegacao em Miller Columns](miller-columns.gif)

> **Navegar por pastas deveria ser assim.**
> Clique em uma pasta e o conteudo aparece na coluna ao lado. Onde voce esta, de onde veio e para onde vai — tudo visivel em uma unica tela. Sem precisar clicar em "voltar" nunca mais.

---

## Por que o SPAN Finder?

| | Windows Explorer | SPAN Finder |
|---|---|---|
| **Miller Columns** | Nao | Navegacao hierarquica em multiplas colunas |
| **Multiplas abas** | Apenas Windows 11 (basico) | Separacao, duplicacao e restauracao completa de abas |
| **Visualizacao dividida** | Nao | Painel duplo com modos de visualizacao independentes |
| **Painel de pre-visualizacao** | Basico | Mais de 10 tipos — imagens, video, audio, codigo, Hex, fontes, PDF |
| **Navegacao por teclado** | Limitada | Mais de 30 atalhos, busca com autocompletar, design keyboard-first |
| **Renomeacao em lote** | Nao | Regex, prefixo/sufixo, numeracao sequencial |
| **Desfazer/Refazer** | Limitado | Historico completo de operacoes (profundidade configuravel) |
| **Temas personalizados** | Nao | 10 temas — Dracula, Tokyo Night, Catppuccin, Gruvbox, Nord e mais |
| **Integracao Git** | Nao | Branch, status e commits em um relance |
| **Conexoes remotas** | Nao | FTP, FTPS, SFTP — credenciais salvas |
| **Areas de trabalho** | Nao | Salvar e restaurar layouts de abas |
| **Status da nuvem** | Overlay basico | Badges de sincronizacao em tempo real (OneDrive, iCloud, Dropbox) |
| **Velocidade de inicializacao** | Lento em pastas grandes | Carregamento assincrono + cancelamento — sem atraso |

---

## Recursos

### Miller Columns — Veja tudo de uma vez

Navegue por hierarquias profundas de pastas sem perder o contexto. Cada coluna representa um nivel de pasta — clique em uma pasta e o conteudo aparece na proxima coluna. Voce sempre sabe onde esta e qual o caminho percorrido.

- Divisores de coluna arrastavel para ajustar a largura
- Equalizar colunas (Ctrl+Shift+=) ou ajustar ao conteudo (Ctrl+Shift+-)
- Rolagem horizontal suave para manter a coluna ativa sempre visivel

### Quatro modos de visualizacao

- **Miller Columns** (Ctrl+1) — Navegacao hierarquica, a assinatura do SPAN Finder
- **Detalhes** (Ctrl+2) — Tabela ordenavel com colunas de nome, data, tipo e tamanho
- **Lista** (Ctrl+3) — Layout compacto em multiplas colunas para varredura de pastas grandes
- **Icones** (Ctrl+4) — Visualizacao em grade com 4 niveis de tamanho, ate 256x256 miniaturas

![Quatro modos de visualizacao](view-modes.gif)

### Multiplas abas + Restauracao completa de sessao

- Abas ilimitadas — cada uma com caminho, modo de visualizacao e historico de navegacao independentes
- **Separar aba**: arraste a aba para uma nova janela — estado totalmente preservado
- **Duplicar aba**: duplicacao exata com caminho e configuracoes
- Salvamento automatico de sessao: feche e reabra o app — todas as abas permanecem intactas

### Visualizacao dividida — Painel duplo de verdade

- Navegacao independente com paineis esquerdo e direito
- Modos de visualizacao diferentes em cada painel (Miller a esquerda, Detalhes a direita)
- Painel de pre-visualizacao individual para cada lado
- Arraste entre paineis para copiar/mover arquivos

![Visualizacao dividida com mais de 14.000 itens](2.jpg)

### Painel de pre-visualizacao — Veja antes de abrir

![Pre-visualizacao de codigo + informacoes Git](5.jpg)

Pressione **Espaco** para Quick Look (estilo macOS Finder):

- **Imagens**: JPEG, PNG, GIF, BMP, WebP, TIFF — resolucao e metadados
- **Video**: MP4, MKV, AVI, MOV, WEBM — controles de reproducao
- **Audio**: MP3, AAC, M4A — artista, album e duracao
- **Texto e codigo**: Mais de 30 extensoes — destaque de sintaxe
- **PDF**: Pre-visualizacao da primeira pagina
- **Fontes**: Amostra de glifos + metadados
- **Hex binario**: Visualizacao de bytes brutos para desenvolvedores
- **Pasta**: Tamanho, numero de itens, data de criacao
- **Hash de arquivo**: Checksum SHA256 + copia com um clique (habilitar nas configuracoes)

### Design keyboard-first

Mais de 30 atalhos para quem nao tira as maos do teclado:

| Atalho | Acao |
|----------|--------|
| Setas direcionais | Navegar entre colunas e itens |
| Enter | Abrir pasta ou executar arquivo |
| Espaco | Alternar painel de pre-visualizacao |
| Ctrl+L / Alt+D | Editar barra de endereco |
| Ctrl+F | Pesquisar |
| Ctrl+C / X / V | Copiar / Recortar / Colar |
| Ctrl+Z / Y | Desfazer / Refazer |
| Ctrl+Shift+N | Nova pasta |
| F2 | Renomear (renomeacao em lote com selecao multipla) |
| Ctrl+T / W | Nova aba / Fechar aba |
| Ctrl+1-4 | Alternar modo de visualizacao |
| Ctrl+Shift+S | Salvar area de trabalho |
| Ctrl+Shift+W | Abrir paleta de areas de trabalho |
| Ctrl+Shift+E | Alternar visualizacao dividida |
| Delete | Enviar para a lixeira |

### Temas e personalizacao

![Temas e personalizacao](themes.gif)

- **10 temas**: Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox, Solarized, Nord, One Dark, Monokai
- **6 niveis de altura de linha** e **6 niveis de tamanho de fonte/icone** — controle independente
- **10 fontes**: Segoe UI Variable, Consolas, Cascadia Code/Mono, D2Coding, JetBrains Mono, Fira Code e mais — cadeia de fallback CJK
- **3 pacotes de icones**: Remix Icon, Phosphor Icons, Tabler Icons
- **9 idiomas**: Portugues, English, 한국어, 日本語, 中文(简体/繁體), Deutsch, Español, Français

### Ferramentas para desenvolvedores

![Visualizador Hex binario](4.jpg)

- **Badges de status Git**: Modified, Added, Deleted, Untracked por arquivo
- **Visualizador Hex dump**: Primeiros 512 bytes em hexadecimal + ASCII
- **Integracao com terminal**: Ctrl+` para abrir o terminal no caminho atual
- **Conexoes remotas**: FTP/FTPS/SFTP — credenciais salvas com criptografia

### Armazenamento em nuvem

- **Badges de status de sincronizacao**: Somente nuvem, Sincronizado, Aguardando upload, Sincronizando
- **OneDrive, iCloud, Dropbox** detectados automaticamente
- **Miniaturas inteligentes**: Usa pre-visualizacoes em cache — evita downloads desnecessarios

### Pesquisa inteligente

- **Consultas estruturadas**: `type:image`, `size:>100MB`, `date:today`, `ext:.pdf`
- **Autocompletar**: comece a digitar em qualquer coluna para filtrar instantaneamente
- **Processamento em segundo plano**: a pesquisa nao trava a interface

### Areas de trabalho — Salvar e restaurar layouts de abas *(v1.2.1.0)*

- **Salvar abas atuais**: clique com o botao direito na aba > "Salvar layout de abas..." ou Ctrl+Shift+S
- **Restaurar instantaneamente**: botao de areas de trabalho na barra lateral ou Ctrl+Shift+W
- **Gerenciar areas de trabalho**: restaurar, renomear e excluir pelo menu de areas de trabalho
- Ideal para alternar entre contextos — "Desenvolvimento", "Edicao de fotos", "Organizacao de documentos"

### Recursos para usuarios avancados

- **Colagem de arquivos virtuais**: Cole com Ctrl+V de sessoes remotas RDP, anexos do Outlook e outras fontes de arquivos virtuais

---

## Desempenho

Projetado para velocidade. Testado com mais de 14.000 itens por pasta.

- E/S assincrona — nunca bloqueia a thread da interface
- Atualizacao de propriedades em lote com overhead minimo
- Selecao com debounce para evitar operacoes duplicadas durante navegacao rapida
- Cache por aba — troca instantanea de abas, sem re-renderizacao
- Carregamento simultaneo de miniaturas com throttling via SemaphoreSlim

---

## Requisitos do sistema

| | |
|---|---|
| **SO** | Windows 10 versao 1903 ou superior / Windows 11 |
| **Arquitetura** | x64, ARM64 |
| **Runtime** | Windows App SDK 1.8 (.NET 8) |
| **Recomendado** | Windows 11 para o efeito de fundo Mica |

---

## Compilar a partir do codigo-fonte

```bash
# Pre-requisitos: Visual Studio 2022 + .NET Desktop + carga de trabalho WinUI 3

# Clonar
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder

# Compilar
dotnet build src/Span/Span/Span.csproj -p:Platform=x64

# Executar testes unitarios
dotnet test src/Span/Span.Tests/Span.Tests.csproj -p:Platform=x64
```

> **Nota**: Aplicativos WinUI 3 nao podem ser executados via `dotnet run`. Use **Visual Studio F5** (requer empacotamento MSIX).

---

## Contribuir

Encontrou um bug? Tem uma sugestao de recurso? [Abra uma issue](https://github.com/LumiBearStudio/SpanFinder/issues) — todo feedback e bem-vindo.

Para configuracao de build, convencoes de codigo e diretrizes de PR, consulte [CONTRIBUTING.md](../CONTRIBUTING.md).

---

## Apoie o projeto

Se o SPAN Finder e util para voce:

- **[Patrocine no GitHub](https://github.com/sponsors/LumiBearStudio)** — pague um cafe, um hamburguer ou um jantar
- **De uma Star neste repositorio** para ajudar mais pessoas a descobrirem o projeto
- **Compartilhe** com colegas que sentem falta do macOS Finder
- **Reporte bugs** — cada issue torna o SPAN Finder mais estavel
- **[Baixe na Microsoft Store](https://apps.microsoft.com/detail/9P7NJ351X9TL)** — avaliacoes na Store ajudam muito na visibilidade

---

## Privacidade e telemetria

O SPAN Finder usa o [Sentry](https://sentry.io) **apenas para relatorios de crash**, e voce pode desativa-lo.

- **O que coletamos**: Tipo de excecao, stack trace, versao do SO, versao do app
- **O que NAO coletamos**: Nomes de arquivos, caminhos de pastas, historico de navegacao, informacoes pessoais
- **Sem analise de uso, sem rastreamento, sem anuncios**
- Todos os caminhos de arquivos nos relatorios de crash sao automaticamente limpos antes do envio
- `SendDefaultPii = false` — nenhum endereco IP ou identificador de usuario e coletado
- **Desativavel**: Configuracoes > Avancado > desligar "Relatorios de crash"
- O codigo-fonte e aberto — verifique voce mesmo em [`CrashReportingService.cs`](../src/Span/Span/Services/CrashReportingService.cs)

Para mais detalhes, consulte a [Politica de Privacidade](../PRIVACY.md).

---

## Licenca

Este projeto e licenciado sob a [GNU General Public License v3.0](../LICENSE).

**Excecao para a Microsoft Store**: O detentor dos direitos autorais (LumiBear Studio) pode distribuir binarios oficiais de acordo com os termos da Microsoft Store. Esses termos nao sao considerados "restricoes adicionais" conforme a Secao 7 da GPL v3. Esta excecao se aplica apenas a distribuicao oficial e nao a forks de terceiros.

**Marca registrada**: O nome "SPAN Finder" e o logotipo oficial sao marcas registradas da LumiBear Studio. Forks devem usar nome e logotipo diferentes. Para a politica completa de marcas, consulte [LICENSE.md](../LICENSE.md).

---

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL">Microsoft Store</a> ·
  <a href="../PRIVACY.md">Politica de Privacidade</a> ·
  <a href="../OpenSourceLicenses.md">Licencas de codigo aberto</a> ·
  <a href="https://github.com/LumiBearStudio/SpanFinder/issues">Reportar bugs e solicitar recursos</a>
</p>
