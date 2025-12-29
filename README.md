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
