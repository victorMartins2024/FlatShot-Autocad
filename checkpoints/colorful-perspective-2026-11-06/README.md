# Colorful FlatShot — checkpoint de perspectiva (06/11/2026)

**Trabalho em andamento. A perspectiva ainda não está homologada nem instalada.**

Este diretório preserva o trabalho feito para o painel **Colorful FlatShot** usado pelo usuário no AutoCAD 2025. A implementação já existente na raiz do repositório é diferente e foi preservada integralmente. Este checkpoint não substitui o `src/ColourfulFlatshot` ou a DLL em `dist/` da raiz.

## Conteúdo

- `src/`: código completo da implementação local ColorFlatshot, com a correção anterior de UCS e o novo caminho experimental de perspectiva.
- `tests/math/`: testes independentes do AutoCAD para projeção, pontos de fuga, recorte e ocultação por profundidade inversa.
- `tests/autocad/`: testes preparados para comparar a projeção com `Editor.PointToScreen`, gerar caixas e silhuetas e verificar cores, layers e plano de inserção.
- `CONTINUAR.md`: situação exata e próximos passos, sujeitos à confirmação do usuário.

## Implementado neste checkpoint

- Detecção automática de vista paralela ou perspectiva no comando `COLORFULFLATSHOTCREATE`.
- Captura das matrizes de projeção e visualização da câmera do sistema gráfico do AutoCAD.
- Projeção perspectiva com divisão homogênea e recorte à frente da câmera.
- Ocultação entre componentes, com índice espacial de triângulos e interpolação de profundidade inversa.
- Arestas de BRep e silhuetas tesseladas, aproximadas por segmentos editáveis conforme a tolerância do painel.
- Saída em bloco ou curvas independentes, seguindo o UCS, layers e cores dos objetos.
- A projeção paralela continua utilizando o caminho anterior por Section.

## Validação real

- Compilação do plugin com SDK .NET 8 e bibliotecas instaladas do AutoCAD 2025: **passou, sem erros e sem avisos**.
- Testes matemáticos: **15/15 passaram**.
- Primeiro teste de integração no AutoCAD 2025: a perspectiva foi ativada e as amostras 0–3 de comparação de câmera passaram; a amostra 4 falhou na tolerância de tela. Os testes de geração de geometria ainda não foram alcançados nessa execução.
- Depois da falha, o teste foi revisado para usar referências proporcionais ao campo da câmera e registrar os valores numéricos. **Essa revisão ainda não foi executada.**
- O controle do computador foi interrompido por Esc. **Não foi instalada esta versão experimental.**

## Compilação

Requer Windows x64, SDK .NET 8 e AutoCAD 2025 (bibliotecas locais, sem redistribuição da Autodesk).

```powershell
dotnet build src/ColorFlatshot.csproj -c Release
dotnet run --project tests/math/PerspectiveMathTests.csproj -c Release
dotnet build tests/autocad/ColorfulPerspectiveTests.csproj -c Release
```

O caminho padrão do AutoCAD é `C:\Program Files\Autodesk\AutoCAD 2025`; pode ser informado com `-p:AutoCADDir=...`.

Para executar `CFPTEST`, carregar a DLL de testes via NETLOAD no AutoCAD e usar um DWG **vazio**. O teste cria somente geometria sintética. Uma eventual autorização de segurança da DLL deve ser feita pelo usuário.

## Limitações

Curvas e silhuetas em perspectiva são aproximações por segmentos, com tolerância configurável. O comportamento em modelos complexos, componentes Plant 3D e cenas grandes ainda precisa de validação. Este checkpoint não é uma versão pronta para produção.
