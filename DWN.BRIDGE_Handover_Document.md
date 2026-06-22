# DWN.BRIDGE - Project Handover & Context Summary

Questo documento riassume la visione, l'architettura tecnica, le strategie di rilascio e i flussi di lavoro di **DWN.BRIDGE** (ex AIBridge). È progettato per essere fornito come "Context Prompt" a un nuovo Agente per iniziare una sessione fresca senza perdere lo storico decisionale.

---

## 1. Visione e Posizionamento Strategico (Il "Manifesto Indie")
**Obiettivo:** Liberare l'IA agentica dal monopolio delle grandi corporate. 
**Problema:** I developer sono costretti a pagare abbonamenti costosi per IDE AI o pagare centinaia di dollari in token API per usare LLM locali. I modelli gratuiti sul web sono potenti, ma isolati nel browser.
**Soluzione (DWN.BRIDGE):** Un'app desktop open-source che fa da ponte tra modelli web gratuiti (guidati tramite Playwright) e il File System / Database locale dell'utente. 
**Tono della Community:** Ribelle, hacker, community-driven. Identità mantenuta anonima dal creatore (*MarckDWN*).

---

## 2. Architettura Tecnica (Client / Server)
### Client (WPF Desktop App)
*   **Core:** C# .NET 10. Usa automazione browser invisibile (Playwright) per intercettare output strutturato da LLM web (es. Gemini) e tradurlo in "Tool Calls" locali (LEggi file, Scrivi file, Esegui SQL).
*   **UI/UX:** Stile dark moderno (`#1E1E2E`). I vecchi MessageBox di Windows sono stati completamente rimpiazzati da una modale custom (`CustomMessageBox`). Pieno supporto alla localizzazione multi-lingua tramite la libreria `TxLib`.
*   **Login:** Supporta Guest (tramite HWID), GitHub OAuth e Magic Link Email. I token JWT permettono l'AutoLogin.

### Server (Backend API)
*   **Core:** ASP.NET Core API collegato a SQL Server tramite Dapper (per massima velocità).
*   **Telemetria non-bloccante:** Non blocchiamo eventuali fork dell'app, ma tracciamo la provenienza. L'app invia un header `X-App-Origin` (valorizzato a `ClickOnce` o `LocalBuild`). Il server aggiorna costantemente la colonna `LastDistribOrigin` della tabella `Utenti` e fa log specifici al login e al ping (`PingAccess`).

---

## 3. Gestione Repositories (Privato vs Community)
Il progetto è diviso in due macro-ambienti per garantire la sicurezza del backend e la trasparenza del client:
1.  **Repo Privato (Server/API):** Contiene tutto il codice ASP.NET Core, le logiche JWT, i secret di autenticazione e gli script di migrazione del Database SQL Server. **Non verrà mai esposto al pubblico.**
2.  **Repo Pubblico (GitHub DWN.BRIDGE):** Contiene unicamente il codice sorgente del Client WPF. Chiunque può clonarlo, fare auditing sulla sicurezza (importante visto che l'app tocca i file locali) e compilarselo da solo.

---

## 4. Strategia di Rilascio e Aggiornamento
La versione di partenza è la **1.0.0.30**.
*   **Installazione Standard (Consigliata):** Distribuita tramite **ClickOnce**. Permette all'utente un'installazione con 1-click e garantisce l'auto-aggiornamento (Auto-Updater nativo) ad ogni avvio. L'header `X-App-Origin` riconosce automaticamente questo flusso.
*   **Installazione Hacker (Source Code):** Compilazione autonoma tramite MSBuild partendo dal Repo GitHub pubblico.

---

## 5. Ecosistema "Community Agents" (Submission & Review)
Gli "Agenti" (es. *Coder*, *SQL Analyst*) sono file Markdown (`.md`) che definiscono le istruzioni base e i tool a disposizione dell'IA.
*   **Utilizzo:** L'utente apre il pannello "My Agents", sfoglia la repository Cloud, scarica gli agenti di suo interesse in locale e li applica alla chat corrente.
*   **Creazione & Condivisione:** Qualsiasi utente può creare un file `.md` locale. Dal client WPF, l'utente può selezionare i propri agenti privati e cliccare su **"Submit"**.
*   **Flusso di Approvazione:** Il Client invia il payload al Server. Gli agenti entrano in uno stato `Pending`. L'admin (MarckDWN) revisiona l'agente per questioni di sicurezza o utilità. Se approvato, diventa pubblico e scaricabile dall'intera community globale.

---
*Fine Documento di Handover*
