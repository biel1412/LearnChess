# ♞ LearnChess

Jogo de xadrez **contra a máquina** que roda no navegador, feito em **C# / ASP.NET Core (.NET 10)**.
O motor de xadrez foi escrito do zero em C# (sem bibliotecas externas).

## Recursos

- **4 níveis de dificuldade**
  | Nível | Como a máquina joga |
  |---|---|
  | Básico | Enxerga 1 lance à frente e às vezes joga um lance aleatório, como um iniciante |
  | Médio | Calcula 2 lances, considera trocas simples e varia as jogadas |
  | Avançado | Calcula 5 lances com busca de capturas (quiescência) |
  | Profissional | Aprofundamento iterativo, tabela de transposição, lance nulo; ~4 s por lance (prof. 9–10+) |
- **Notação algébrica** de cada lance (SAN: `e4`, `Nf3`, `exd5`, `O-O`, `e8=Q+`, `Qxf7#`), em uma lista com número do lance, brancas e pretas.
- **Anotações por lance**: símbolos `!!` `!` `!?` `?!` `?` `??` + **comentário livre**.
- **Anotações gerais da partida** (plano, abertura, o que aprendeu).
- Tudo é **salvo em arquivo JSON** automaticamente a cada lance/edição (`data/games/<id>.json`).
- Exportação da partida em **JSON** e **PGN** (com anotações e comentários).
- Revisão de lances (⏮ ◀ ▶ ⏭ ou setas do teclado), desfazer lance, desistir, virar tabuleiro.
- Detecção de xeque-mate, afogamento, tripla repetição, regra dos 50 lances e material insuficiente.
- Clique ou arraste para mover; escolha da peça na promoção.

## Como rodar

Pré-requisito: [.NET SDK 10](https://dotnet.microsoft.com/download).

```bash
cd src/LearnChess.Web
dotnet run
```

Abra **http://localhost:5251** no navegador.

As partidas ficam em `src/LearnChess.Web/data/games/`. Para mudar a pasta, edite
`LearnChess:DataDirectory` no `appsettings.json`.

### Teste do gerador de lances (perft)

```bash
cd src/LearnChess.Web
dotnet run -c Release -- perft
```

Compara a contagem de posições com valores de referência conhecidos (posição inicial, Kiwipete etc.).

## Formato do JSON

```json
{
  "id": "20261124-223345-a0d420",
  "level": "Profissional",
  "levelName": "Profissional",
  "playerColor": "white",
  "status": "EmAndamento",
  "result": "*",
  "notes": "Treino de Italiana",
  "moves": [
    {
      "ply": 1,
      "moveNumber": 1,
      "color": "white",
      "playedBy": "jogador",
      "san": "e4",
      "uci": "e2e4",
      "fenAfter": "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
      "annotation": "!",
      "comment": "Abertura clássica, controla o centro.",
      "engineEval": null,
      "engineDepth": null
    }
  ]
}
```

`engineEval` é a avaliação da máquina em centipeões (positivo = vantagem das brancas) nos lances que ela jogou.

## Estrutura

```
src/LearnChess.Web/
├── Chess/                 # motor de xadrez
│   ├── Board.cs           # tabuleiro, geração de lances, FEN, regras
│   ├── Notation.cs        # notação algébrica (SAN) e UCI
│   ├── Evaluation.cs      # avaliação (material + tabelas de posição)
│   ├── Engine.cs          # busca alfa-beta
│   ├── DifficultyLevel.cs # configuração dos 4 níveis
│   └── Perft.cs           # testes do gerador de lances
├── Games/                 # partidas, persistência JSON, PGN
├── wwwroot/               # interface web (HTML/CSS/JS)
└── Program.cs             # API REST
```

## API

| Método | Rota | Descrição |
|---|---|---|
| GET | `/api/levels` | níveis disponíveis |
| GET | `/api/games` | partidas salvas |
| POST | `/api/games` | nova partida `{ "level": "Medio", "playerColor": "white" }` |
| GET | `/api/games/{id}` | estado da partida |
| POST | `/api/games/{id}/moves` | lance do jogador `{ "uci": "e2e4" }` |
| POST | `/api/games/{id}/engine-move` | máquina joga |
| POST | `/api/games/{id}/undo` | desfaz o último lance do jogador |
| POST | `/api/games/{id}/resign` | desistir |
| PUT | `/api/games/{id}/moves/{ply}/annotation` | `{ "annotation": "!", "comment": "..." }` |
| PUT | `/api/games/{id}/notes` | `{ "notes": "..." }` |
| GET | `/api/games/{id}/export/json` | baixar JSON |
| GET | `/api/games/{id}/export/pgn` | baixar PGN |
| DELETE | `/api/games/{id}` | excluir partida |
