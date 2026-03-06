# SPAN Finder

**Um explorador de arquivos Miller Columns ultrarrápido para Windows, feito para usuários avançados que não aceitam compromissos.**

[English](../README.md) | [한국어](README.ko.md) | [日本語](README.ja.md) | [中文(简体)](README.zh-CN.md) | [中文(繁體)](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | Português

O SPAN Finder reinventa a navegação de arquivos no Windows. Inspirado pela elegância da visualização em colunas do macOS Finder e turbinado com recursos que o Windows Explorer nunca teve — múltiplas abas, visualização dividida, operações assíncronas e fluxos de trabalho orientados pelo teclado.

> **Por que se contentar com o Windows Explorer?**

---

## Por que SPAN Finder?

| | Windows Explorer | SPAN Finder |
|---|---|---|
| **Miller Columns** | Não | Sim — navegação hierárquica multi-colunas |
| **Multi-abas** | Apenas Windows 11 (básico) | Abas completas com separação, duplicação e restauração |
| **Visualização dividida** | Não | Painel duplo com modos de visualização independentes |
| **Painel de visualização** | Básico | 10+ tipos — imagens, vídeo, áudio, código, Hex, fontes, PDF |
| **Navegação por teclado** | Limitada | 30+ atalhos, busca preditiva, design keyboard-first |
| **Renomeação em lote** | Não | Regex, prefixo/sufixo, numeração sequencial |
| **Desfazer/Refazer** | Limitado | Histórico completo de operações (profundidade configurável) |
| **Temas personalizados** | Não | 10 temas — Dracula, Tokyo Night, Catppuccin, Gruvbox, Nord e mais |
| **Integração Git** | Não | Branch, status, commits em um relance |
| **Conexões remotas** | Não | FTP, FTPS, SFTP com credenciais salvas |
| **Status da nuvem** | Overlay básico | Badges de sincronização em tempo real (OneDrive, iCloud, Dropbox) |
| **Velocidade de início** | Lento em diretórios grandes | Carregamento assíncrono + cancelamento — sem atraso |

---

## Recursos principais

### Miller Columns — Veja tudo de uma vez

Navegue por hierarquias profundas de pastas sem perder o contexto. Cada coluna representa um nível — clique em uma pasta e seu conteúdo aparece na próxima coluna.

### Quatro modos de visualização

- **Miller Columns** (Ctrl+1) — Navegação hierárquica
- **Detalhes** (Ctrl+2) — Tabela ordenável
- **Lista** (Ctrl+3) — Layout multi-colunas de alta densidade
- **Ícones** (Ctrl+4) — Visualização em grade com 4 opções de tamanho

### Painel de visualização — Veja antes de abrir

Pressione **Espaço** para Quick Look (estilo macOS Finder):

- Imagens, vídeo, áudio, texto/código, PDF, fontes, Hex binário, informações de pasta

### Temas e personalização

- **10 Temas**: Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox, Solarized, Nord, One Dark, Monokai
- **6 níveis de altura de linha** e **6 níveis de tamanho de fonte/ícone**
- **10 fontes**: Segoe UI Variable, Consolas, Cascadia Code/Mono, D2Coding, JetBrains Mono e mais — com fallback CJK automático
- **9 idiomas**: English, 한국어, 日本語, 中文(简/繁), Deutsch, Español, Français, Português

### Ferramentas para desenvolvedores

- Badges de status Git, visualizador Hex dump, integração com terminal, conexões FTP/SFTP

---

## Requisitos do sistema

| | |
|---|---|
| **SO** | Windows 10 versão 1903+ / Windows 11 |
| **Arquitetura** | x64, ARM64 |
| **Runtime** | Windows App SDK 1.8 (.NET 8) |

---

## Compilar a partir do código-fonte

```bash
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder
dotnet build src/Span/Span/Span.csproj -p:Platform=x64
```

> **Nota**: Aplicativos WinUI 3 não podem ser iniciados via `dotnet run`. Use **Visual Studio F5** (empacotamento MSIX necessário).

---

## Contribuir

Consulte [CONTRIBUTING.md](CONTRIBUTING.md).

## Licença

[GNU General Public License v3.0](LICENSE) (com exceção de distribuição na Microsoft Store)

O nome "SPAN Finder" e o logotipo oficial são marcas registradas da LumiBear Studio. Veja [LICENSE.md](LICENSE.md).

---

[Microsoft Store](https://www.microsoft.com/store/apps/9P42MFRMH07X) | [GitHub](https://github.com/LumiBearStudio/SpanFinder) | [Reportar bugs](https://github.com/LumiBearStudio/SpanFinder/issues) | [Privacidade](https://github.com/LumiBearStudio/SpanFinder/blob/main/github-docs/PRIVACY.md)
