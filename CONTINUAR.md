# Continuar o desenvolvimento

## Ponto de partida

Versão 0.2.3 do Colourful Flatshot para AutoCAD 2023 Windows x64 / .NET Framework 4.8. CF e COLOURFULFLATSHOT abrem a paleta; CFS_GENERATE gera o bloco 2D. A instalação local foi atualizada na pasta confiável Program Files/Autodesk/ApplicationPlugins/ColourfulFlatshot.bundle, com carregamento automático.

## Preferências já definidas

- Aparência semelhante à janela Propriedades do AutoCAD: cinza-escuro, grade compacta, categorias recolhíveis.
- Preservar cores RGB/ACI e layers por padrão.
- Ocultas personalizadas, ACI 8, tipo HIDDEN; prioridade das visíveis nas sobreposições.
- Capturar a vista atual e toda a geometria 3D compatível/visível no Model Space, sem seleção manual.
- Não reintroduzir a seção Projeção nem campos técnicos de tolerância/sobreposição na paleta.
- Manter as medidas do Model em escala 1:1.
- O usuário quer rapidez comparável ao FLATSHOT padrão. Essa equivalência ainda não foi atingida ou comprovada.

## Arquivos

- Plugin.cs: comandos, abertura da PaletteSet, fila de geração e pedido de inserção.
- FlatshotPalette.cs: grade de propriedades e opções.
- FlatshotEngine.cs: leitura do modelo, cache por geração, índice espacial, ocultação, cores/layers e inserção do bloco.
- ProjectionMath.cs: cálculo de intervalos ocultos independente da API.
- tests/EngineChecks.cs: verificações dentro do motor do AutoCAD.
- packaging/PackageContents.xml: manifesto de carregamento automático para R24.2.
- dist/ColourfulFlatshot.dll: binário 0.2.3 que acompanha o código.

## Próximas prioridades

1. Medir a geração no DWG real usado pelo usuário e comparar com o FLATSHOT nativo no mesmo modelo e vista; ainda não há essa comparação.
2. Melhorar desempenho e qualidade de curvas sem retirar a preservação de cores/layers.
3. Validar o fluxo completo do botão na interface gráfica: ponto de inserção, foco, cancelamento, UNDO, acoplamento e escalas de tela.
4. Investigar o encerramento do Core Console ao abortar resultados grandes consecutivos na versão anterior. Gerações confirmadas e o teste separado de rollback pequeno passaram, mas rollback grande continua sem validação conclusiva.
5. Corrigir e testar outros casos geométricos conforme modelos reais: xrefs, cores por face e blocos dinâmicos não estão cobertos integralmente.

## Cuidados para retomar

Antes de mexer, leia README.md e CHANGELOG.md. Não prometa equivalência ao FLATSHOT nativo: a geração atual é aproximada por malha/segmentos. Não copie DLLs Autodesk para o repositório. Use /isolate nos testes do Core Console para evitar alterar FILEDIA ou outras preferências do perfil normal. Não desative SECURELOAD para instalar. Preserve os DWGs do usuário e teste em cópias/bancos temporários.
