# Ponto de retomada

O usuário solicitou o envio deste estado ao GitHub e pediu que, após a confirmação do envio, o trabalho aguardasse nova autorização.

**Não continuar o desenvolvimento, testes no computador ou instalação até a confirmação do usuário.**

## Objetivo pendente

Concluir perspectiva real com pontos de fuga no painel Colorful FlatShot, mantendo a vista/câmera e inserindo o resultado no UCS atual. O usuário confirmou que usa esse painel, e não o plugin separado Flatshot Layers.

## Estado técnico

1. O plugin experimental compila sem avisos.
2. Os 15 testes de geometria matemática passaram.
3. A execução inicial de `CFPTEST` falhou em `camera matrix matches AutoCAD PointToScreen sample 4`, antes de criar os sólidos de teste.
4. O teste atualizado usa referências proporcionais a `gs.FieldWidth` e `gs.FieldHeight` para reduzir efeito de arredondamento de pixels. Ele também registra parâmetros e diferenças. A causa da falha ainda não está confirmada.
5. Não aumentar a tolerância do teste nem declarar câmera correta sem investigar os valores e a sincronização entre ViewTableRecord, GraphicsSystem.View e PointToScreen.
6. A geração nativa de caixas, ocultação de faces, silhueta de esfera e saída em UCS rotacionado ainda precisa ser executada no AutoCAD.
7. Há uma implementação separada na raiz do repositório. Conciliar com ela antes de integrar este checkpoint, em vez de substituir seus arquivos automaticamente.

## Ao receber autorização para retomar

- Executar o teste revisado em um desenho vazio, conferir matrizes e escala contra a câmera do AutoCAD e corrigir diferenças reais.
- Exercitar extração de BRep, tesselação, tangências, silhuetas, linhas ocultas, recorte e saída em bloco/curvas.
- Verificar que a vista, o UCS, o alvo e a lente permanecem iguais.
- Testar o painel com seleção manual e seleção de todos os objetos.
- Somente depois de validar, gerar instalador e atualizar o bundle do Colorful FlatShot.
- Não encerrar ou salvar desenhos do usuário para reiniciar o AutoCAD.

A instalação anterior com correção de UCS/vistas paralelas foi mantida. O suporte experimental à perspectiva não foi instalado.
