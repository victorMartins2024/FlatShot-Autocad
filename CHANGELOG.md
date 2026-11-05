# Colourful Flatshot 0.2.3 — alterações e verificação

- Removida a seção Projeção da paleta, incluindo Sobreposição e Tolerância. A precisão é interna e a prioridade das linhas visíveis permanece ativa.
- A paleta informa Escala: 1:1 (Model). O bloco mantém as unidades do modelo e as dimensões projetadas. O zoom e a escala anotativa não aplicam ampliação/redução à geometria.
- Busca de ocultações por hierarquia de limites, com exclusão de regiões que não cruzam cada segmento e de superfícies inteiramente atrás dele.
- Menos alocações e chaves de texto na leitura da malha e no teste de ocultação.
- Geometria de blocos repetidos reutilizada dentro de cada geração; uma nova geração refaz o cache, evitando resultados antigos após editar o modelo.
- Consultas de cancelamento à interface limitadas a uma a cada 50 ms durante o processamento; a operação continua cancelável.

## Teste de desempenho

AutoCAD Core Console 2023, perfil de teste separado com /isolate; banco de desenho temporário. Comparação com a versão 0.2.2, três gerações alternadas de cada versão. O tempo exclui a abertura do AutoCAD e a escolha do ponto de inserção. Não utiliza o DWG do usuário.

120 instâncias de um bloco local contendo uma caixa e um cilindro (240 sólidos), em vista inclinada:

| Execução | 0.2.2 | 0.2.3 |
| --- | ---: | ---: |
| 1 | 547 ms | 487 ms |
| 2 | 583 ms | 457 ms |
| 3 | 599 ms | 448 ms |
| Mediana | 583 ms | 457 ms |

Redução mediana de aproximadamente 22% no tempo. Cada geração produziu 13.240 trechos visíveis e 6.640 ocultos, em ambas as versões. Isso compara contagens, não comprova identidade geométrica ponto a ponto.

No primeiro teste com 120 sólidos independentes, o ganho foi pequeno e variou entre execuções; os resultados não justificam prometer uma melhoria uniforme em todo desenho. O tempo do FLATSHOT nativo não foi medido e não se afirma equivalência de velocidade com ele.

Uma tentativa de comparar descartando várias gerações grandes consecutivas provocou encerramento do Core Console após calcular o resultado da versão anterior. A medição acima usou gerações confirmadas normalmente no banco temporário. O teste separado de cancelamento e rollback pequeno passou; rollback de resultados grandes ainda requer validação adicional.

## Verificações

Os testes existentes de cores RGB/ACI, layers, ocultas, sobreposição parcial, esfera, cilindro, superfície, malha, bloco rotacionado e UCS passaram. Incluída verificação explícita de ScaleFactors = (1,1,1) no bloco inserido. Verificados na interface os padrões, a ausência da categoria Projeção, a ligação das opções e a proteção do fundo escuro.

O projeto compilou Release/x64 contra as bibliotecas do AutoCAD 2023. Permanece o aviso MSB3644 sobre ausência do Targeting Pack 4.8 neste ambiente. As limitações geométricas do README continuam válidas: curvas aproximadas, cores por objeto, sem xrefs ou conteúdo 2D.

A atualização é instalada em C:\Program Files\Autodesk\ApplicationPlugins\ColourfulFlatshot.bundle e passa a valer após reiniciar o AutoCAD. Os testes não alteram os desenhos abertos pelo usuário.
