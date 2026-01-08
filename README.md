# 🤖 MARL Balloon-Popping Robot Competition

## Overview

This project utilizes **Unity** and the **ML-Agents** library to test a Multi-Agent Reinforcement Learning (MARL) competition. The environment consists of two opposing teams of robots fighting in a closed arena.

### The Game Rules
* **The Objective:** Completely eliminate the opposing team. A team wins when all robots on the opposing team have lost all their balloons.
* **The Agents:** Simple cylinder robots with linear and angular velocity control.
    * **Sensors:** "Simulated" Lidar (Ray Perception Sensor) to detect walls, enemies, and balloons.
    * **Equipment:** A **Spike** on the front and a row of **3 Balloons** on the back.
    * **Abilities:** A "Speed Boost" (short duration, fixed cooldown).
* **Combat Mechanics:**
    * **Popping:** To damage an enemy, a robot must drive its spike into an enemy's balloon.
    * **Elimination:** When a robot loses all 3 balloons, it enters a "Dead" state. It stops moving and remains in the arena as a static physical obstacle.
* **The Arena:**
    * Contains static walls.
    * **Restocking:** There are 2 zones where balloons spawn periodically. Robots can pick these up to replenish health (Max capacity: 3 balloons).
    * **Spawning:** Robots start at random positions at the beginning of every match.

---

## 🛠️ Setup Instructions

### 1. Unity Environment
1.  **Install Unity Editor:**
    * Version: **`6000.2.7f2`** (Install via Unity Hub).
2.  **Install ML-Agents:**
    * Open the project.
    * Go to `Window` > `Package Manager`.
    * Click the `+` icon in the top left.
    * Search for ML Agents and install it.

### 2. Python Environment
**Prerequisites:**
* Install **Python 3.10.11** (or another version of 3.10, if you can't find this one).

**Step-by-Step Setup:**

1.  **Create the Virtual Environment (Venv):**
    Open your terminal at the **root** of this project folder.

    * **Windows:**
        ```bash
        python -m venv venv
        ```
    * **Mac / Linux:**
        ```bash
        python3 -m venv venv
        ```

2.  **Activate the Environment:**

    * **Windows:**
        ```bash
        .\venv\Scripts\activate
        ```
    * **Mac / Linux:**
        ```bash
        source venv/bin/activate
        ```

3.  **Install Dependencies:**
    Once the environment is active (you should see `(venv)` in your terminal), run:
    ```bash
    pip install -r requirements.txt
    ```

---

## ✅ To-Do List
- [ ] Treinar durante 30min para verificar se há algo de muito errado e se é necessário fazer ajustes de performance (reduzir número de arenas, reduzir número de raios nos sensores...)
- [x] Criar nova arena (maior, não simétrica, mais paredes de obstáculos...)
- [x] Adicionar 2 zonas de spawn de balões à nova arena, aumentar o size do vetor de observations em cada agente de 11 para 14 (3 observations para a nova zona), e adicionar as 2 zonas à lista de health spawners no Arena Manager dos environments
- [x] Mudar a lógica de jogo de eliminar a outra equipa para ser a equipa que tem mais balões ao fim de X tempo (Os agentes não começam com o número máximo de balões que podem suportar. Eliminar o step penalty, uma vez que agora cada partida tem um tempo fixo para terminar e por isso penalizar por estar a demorar demasiado tempo já não faz sentido, isso fica embutido na própria lógica do jogo)
- [x] Implementar lógica "Battle Royale", de obrigar os agentes a se encontrarem no centro à medida que o tempo passa (force field, vento, chão inclinar..., o que parecer que melhor faz a lógica da maneira mais simples)
- [ ] O cenário agora é muito mais complexo e então provavelmente precisamos de um bom "Curriculum" para ajudar o treino, tipo fazer os robos spawnarem mais no centro nas iterações iniciais e depois ir aumentando até a arena toda como faziamos, limitando as capacidades de um robô ou d a complexidade da arena (menos obstáculos ou assim) no início e ir aumentando gradualmente... É treinar e ver se é preciso e o que faria mais sentido
- [x] O ELO provavelmente será difícil de ler aqui, eu acho que dá para colocar métricas customizadas. Se der, era bom colocar algumas que fossem bem mais específicas ao jogo para avaliar performance (tipo número de vitórias por equipa, estatísticas desse género)
- [ ] Treinar e ajustar o que for necessário (Modificar curriculum, ajustar algum reward...)
