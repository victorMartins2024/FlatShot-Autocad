# Colourful Flatshot — AutoCAD 2023

Versão salva: **0.2.3**. Plugin C# para AutoCAD 2023 completo, Windows x64, .NET Framework 4.8. Este repositório contém o código-fonte, os testes geométricos, os resultados registrados e a DLL da versão atual.

## Uso

- COLOURFULFLATSHOT ou CF abre a paleta acoplável com aparência escura de grade de propriedades.
- Configure a preservação de cores e layers e a exibição das linhas ocultas.
- Clique em Gerar Flatshot e escolha o ponto de inserção no Model Space. CFS_GENERATE também inicia a geração.
- A saída é um novo bloco CF_ de linhas 2D no XY do UCS, em escala 1:1 nas unidades do modelo. Zoom e escala anotativa não redimensionam a geometria.
- O modelo original é preservado. Resultados anteriores CF_ não são incluídos em novas gerações.

Padrões: preservar cores e layers; ocultas personalizadas em ACI 8 / HIDDEN; prioridade das linhas visíveis nas sobreposições. Não existe uma seção Projeção na paleta. A precisão é interna (tolerância 0,01 nas unidades do desenho).

## Compilar

Pré-requisitos: AutoCAD 2023 ou referências gerenciadas ObjectARX 2023, Visual Studio 2022 com desenvolvimento desktop .NET e .NET Framework 4.8 Developer Pack/Targeting Pack.

Abra ColourfulFlatshot.sln e compile Release | x64. No Developer PowerShell do Visual Studio, na raiz do repositório:

```powershell
msbuild .\ColourfulFlatshot.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64
```

O projeto referencia AcMgd.dll, AcDbMgd.dll, AcCoreMgd.dll e AcDbMgdBrep.dll em C:\Program Files\Autodesk\AutoCAD 2023, todas com Copy Local = False. Para outro caminho, acrescente `'/p:AutoCADDir=D:\SDK\ObjectARX2023\inc'`.

Saída: src\ColourfulFlatshot\bin\Release\ColourfulFlatshot.dll. Não distribua as bibliotecas Autodesk. Não requer NuGet.

## Instalar

Para experimentar manualmente, carregue a DLL compilada por NETLOAD em uma pasta confiável. Há também uma cópia da DLL 0.2.3 em dist/ColourfulFlatshot.dll.

Para carregamento automático:

1. Crie ColourfulFlatshot.bundle e copie packaging/PackageContents.xml para a raiz dessa pasta.
2. Dentro dela, crie Contents/Windows e copie a DLL como ColourfulFlatshot.v023.dll, conforme o manifesto.
3. Copie a pasta bundle completa para C:\Program Files\Autodesk\ApplicationPlugins com permissão de administrador.
4. Reinicie o AutoCAD e execute CF. Essa pasta é reconhecida como confiável pelo AutoCAD 2023.

Se já houver uma instalação, atualize a existente e evite manter dois manifestos ativos do mesmo plugin em pastas ApplicationPlugins diferentes. Uma DLL já carregada requer reiniciar o AutoCAD para atualização. Não é necessário desativar SECURELOAD. AutoCAD LT 2023 não é suportado.

## Estado e limites

O motor é próprio: usa facetamento, arestas BRep e cálculo de ocultações; não é o motor nativo FLATSHOT. Curvas e silhuetas são aproximadas por segmentos. Cores são mantidas por objeto, sem materiais ou cores individuais por face. Superfícies são tratadas como opacas.

Processa sólidos, superfícies e malhas visíveis de todo o Model Space, incluindo blocos locais. Não projeta xrefs, textos, cotas, hachuras, entidades 2D, proxies ou nuvens de pontos. Perspectiva e geração em Layout não são suportadas. Faces de malha côncavas/não planas e estados especiais de blocos dinâmicos requerem validação adicional.

Limites atuais: 32 níveis de blocos, 150.000 triângulos e 100.000 segmentos candidatos. Os ajustes da paleta vivem apenas na sessão.

## Verificação registrada

Compilação Release/x64 contra a API real 24.2.53 do AutoCAD 2023. O ambiente de origem tinha aviso MSB3644 por ausência do Targeting Pack 4.8; recompilar com o Developer Pack é recomendado.

Testes geométricos executados no AutoCAD Core Console 2023: cores RGB/ACI, layers, ocultação entre sólidos, opções, rollback pequeno, cancelamento, esfera, cilindro, superfície, malha, blocos com rotação/escala, UCS e escala de saída 1:1. Código em tests/EngineChecks.cs e relatório em tests/engine-checks.txt. O comando da DLL de testes é CFS_ENGINE_CHECKS; compile o arquivo junto com FlatshotEngine.cs e ProjectionMath.cs, referenciando AcCoreMgd, AcDbMgd e AcDbMgdBrep. Use banco temporário, não o desenho de produção. Em testes automatizados, utilize /isolate no Core Console para separar o perfil de teste.

Na comparação registrada com 120 blocos repetidos/240 sólidos, a mediana caiu de 583 ms para 457 ms. Isso não comprova velocidade igual à do FLATSHOT nativo nem ganho uniforme em desenhos diferentes. Os resultados detalhados e a limitação do teste de rollback grande estão em CHANGELOG.md.

Veja CONTINUAR.md para retomar o trabalho. Nenhum DWG do usuário ou biblioteca Autodesk é incluído.
