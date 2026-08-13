# 🤖 MARL Balloon-Popping Robot Competition

## 🎮 Project Results

[Demo Video](https://github.com/user-attachments/assets/465c6651-6bd2-4a7c-a395-b5ccfd9b65cd)

- Presentation Slides: available [here](repo_description/presentation_slides.pdf)
- Report in Article Style: available [here](repo_description/report.pdf)

---

## 📖 Overview

This project implements a **Multi-Agent Reinforcement Learning (MARL)** competition using **Unity** and the **ML-Agents Toolkit**. It simulates a "Battle Royale" style tournament where autonomous robot agents compete in teams to collect resources and eliminate opponents.

The agents are trained using **POCA (Posthumous Credit Assignment)**, utilizing a **Curriculum Learning** strategy that progressively increases environmental complexity (Spawning -> Wind Forces -> Obstacles).

### The Game Rules
* **Objective:** The team with the highest number of balloons at the end of the **45-second match** wins.
* **Agents:** Differential drive robots equipped with:
    * **Sensors:** Ray Perception Sensors (Simulated Lidar) for detecting walls, enemies, and items.
    * **Spike (Offense):** Used to pop enemy balloons on contact.
    * **Balloons (Health/Score):** Agents start with 3 balloons. Losing all balloons does not eliminate the agent. Reaching a max amount of 5 balloons prevents an agent from collecting more.
    * **Boost:** A temporary (2s) speed boost ability (5s cooldown).
* **The Arena:**
    * **Battle Royale Wind:** A centripetal force pushes agents towards the center as the match progresses, shrinking the playable area, and promoting combat between agents.
    * **Restocking Zones:** Two central spawners periodically generate Health Balloons.
    * **Obstacles:** Asymmetric walls that require complex navigation (enabled in later training stages). The walls and restocking zones are always fixed.
    * **Teams:** Agents begin each match with a random position and orientation. The game supports variable team sizes without retraining. It requires training if the total number of teams changes since it affects the size of the observation space of agents. 

---

## 📂 Directory Organization

* **`Assets/Scripts/Bot/`**: Contains the core logic scripts.
    * `BattleBotAgent.cs`: The Agent script handling observations, actions, and rewards.
    * `BattleArena.cs`: Manages the match loop, spawning, scoring, and environmental forces (Wind).
    * `BalloonSpawner.cs`: Handles resource regeneration.
    * `SpikeHitbox.cs`: Detects collisions with enemy balloons.
* **`Assets/ML-Configs/`**: Contains the training configuration files.
    * `BattleBot.yaml`: The main config file defining hyperparameters, network architecture (512 units), and the 3-stage Curriculum.

---

## 🛠️ Setup Instructions

### 1. Unity Environment
* **Unity Version:** `6000.2.7f2`
* **Packages:**
    * `com.unity.ml-agents` (Version `2.0.2`)
    * `com.unity.ai.navigation`

### 2. Python Environment
To train the agents or run the inference engine, you need a Python environment with the `mlagents` package.

**Prerequisites:** Python 3.10.x

#### Step-by-Step Setup:

1.  **Create a Virtual Environment:**
    Open your terminal at the project root.
    ```bash
    # Windows
    python -m venv venv

    # Mac / Linux
    python3 -m venv venv
    ```

2.  **Activate the Environment:**
    ```bash
    # Windows
    .\venv\Scripts\activate

    # Mac / Linux
    source venv/bin/activate
    ```

3.  **Install Dependencies:**
    ```bash
    pip install mlagents==0.27.0
    pip install torch~=1.7.1
    # OR with the provided requirements.txt
    pip install -r requirements.txt
    ```

---

## 🚀 How to Run

### Training Mode
To start a new training session with the defined Curriculum:

1.  Open the terminal and activate your virtual environment.
2.  Run the following command:
    ```bash
    mlagents-learn Assets/ML-Configs/BattleBot.yaml --run-id=MyTrainingSession --force
    ```
3.  Press **Play** in the Unity Editor when prompted.

#### Advanced Training Commands
*   **Resume an interrupted training:**
    ```bash
    mlagents-learn Assets/ML-Configs/BattleBot.yaml --run-id=MyTrainingSession --resume
    ```
*   **Start a NEW run initialized from an old model (Fine-tuning):**
    ```bash
    mlagents-learn Assets/ML-Configs/BattleBot.yaml --run-id=NewSession --initialize-from=MyTrainingSession
    ```

### Visualization (TensorBoard)
To view training metrics (Reward, ELO, Win Rates, etc.):

```bash
tensorboard --logdir results
```
Open the provided URL (usually `http://localhost:6006`) in your browser.

### Inference Mode (Play/Demo)
To watch the trained agents play:

1.  Find the `.onnx` model file in `results/MyTrainingSession/BattleBot/`.
    > **Note:** The best version evaluated in our report is located in `results/new_training/`.
2.  Copy it to the `Assets/Models/` folder in Unity.
3.  Select the **BattleBot** prefab (or agents in the scene).
4.  Drag the `.onnx` file into the **Model** field of the `Behavior Parameters` component.
5.  Set **Behavior Type** to `Inference Only`.
6.  Press **Play** in Unity.

---

## 👥 Authors
* **David Amorim Cordeiro** (up202108820@up.pt)
* **Ema Maria Monteiro Martins** (up202402794@up.pt)
* **Isabel Maria Couto da Silva** (up201904925@up.pt)
* **Pedro Miguel Meruge Ferreira** (up202409828@up.pt)

> Forked from [https://github.com/davehubber/RI-Final-Project](https://github.com/davehubber/RI-Final-Project)

Intelligent Robotics Final Project - Faculty of Engineering, University of Porto 2025/2026 