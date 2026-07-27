# Psychology Game Tools (Plain English Queries)

RimSynapse Psychology registers two advanced **game tools** with Core's native tool-calling engine. These tools expose the colonist's traits, sanity thresholds, social networks, and interaction history directly to the AI Storyteller and dialogue handlers.

Below is the list of Psychology's game tools, along with **plain English instructions** you can use to query them.

---

## 1. Colonist Psychological Profile

### `get_colonist_psychology_profile`
- **What it does**: Gathers complete psychological stats for a pawn on the map:
  - **Identity**: Name, gender, age, title, faction, childhood/adulthood backstory.
  - **Sanity and Break Risk**: Current mood percentage, break thresholds, active break categories (Homicidal, Suicidal, IssueAverse), pending predictions, and euphoria/bipolar states.
  - **Evolving Traits**: Both vanilla traits and dynamic trait records (added by the AI during play).
  - **Weighted Memories**: Detailed list of recent memories, remaining weights, and tags, along with the top 5 memory burdens.
  - **Social Network**: Opinion rating, relationship status, familiarity scores, trust values, opinion integrals, opinion trajectories, and shared relationship memories with other colonists.
  - **Therapy Sessions**: Chronological list of recent therapist-led counseling transcripts.
- **Plain English Prompt Examples**:
  - *"Query John's psychological profile."*
  - *"Check John's sanity level, break category, and recent memories."*
  - *"How does John feel about Sarah? Check their relationship trust and familiarity."*
  - *"Tell me about John's dynamic traits and recent therapy session transcripts."*

---

## 2. Play Log and Social Chit-Chat

### `get_recent_social_interactions`
- **What it does**: Scans vanilla RimWorld's native play log to list recent social chatter (insults, compliments, chats, deep talks) chronologically. It can filter interactions involving a specific pawn name.
- **Plain English Prompt Examples**:
  - *"What are the recent social interactions on our map?"*
  - *"List the last 15 chats or insults that occurred in the colony."*
  - *"Show me recent social logs involving John."*
