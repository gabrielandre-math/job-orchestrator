# Bench dos modelos locais (05/11/2026 03:17)

`jo llm bench`: 50 casos (20 job.extract, 15 email.classify, 15 question.map), temperatura zero, sem raciocínio longo, um modelo por vez na RTX 4060 Ti 8 GB.
Acerto é a fração dos campos certos em cada caso; VRAM é o que o modelo somou ao que o desktop já usava.

| Modelo | JSON válido | job.extract | email.classify | question.map | Geral | p50 | p95 | Carga fria | Na GPU | VRAM |
|---|---|---|---|---|---|---|---|---|---|---|
| qwen3:4b | 50/50 | 84% | 97% | 100% | 93% | 0.7 s | 1.8 s | 3 s | 82% | 2.9 GB |
| qwen3.5:4b | 50/50 | 98% | 93% | 100% | 97% | 1.0 s | 2.6 s | 4 s | 71% | 3.1 GB |
| qwen3.5:9b | 50/50 | 96% | 100% | 100% | 99% | 3.0 s | 6.7 s | 8 s | 35% | 3.1 GB |
| gemma4:e4b | 50/50 | 98% | 100% | 100% | 99% | 0.8 s | 1.4 s | 10 s | 100% | 3.7 GB |

## Erros de qwen3:4b

- job.extract, "Senior Software Engineer (C#)": salary_max: 6001 (esperado 6000)
- job.extract, "Tech Lead .NET": contract: "contractor" (esperado "pj"); salary_max: 25001 (esperado 25000)
- job.extract, "Estágio em Desenvolvimento de Software": english: "none" (esperado "unknown"); salary_max: 2001 (esperado 2000)
- job.extract, "Staff Engineer, Platform": contract: "clt" (esperado "unknown")
- job.extract, "Backend Developer": seniority: "junior" (esperado "unknown"); contract: "clt" (esperado "unknown"); salary_max: 550 (esperado 55000)
- job.extract, "Engenheiro de Software Pleno - Java": english: "none" (esperado "unknown")
- job.extract, "Desenvolvedor .NET Sênior": contract: "contractor" (esperado "pj"); salary_min: 12000 (esperado 120); salary_max: 12000 (esperado 120)
- job.extract, "Junior Backend Engineer": salary_max: 650 (esperado 6500)
- job.extract, "Principal Engineer": seniority: "senior" (esperado "staff"); contract: "clt" (esperado "unknown")
- job.extract, "Desenvolvedor Back-end": seniority: "junior" (esperado "unknown"); salary_max: 7001 (esperado 7000)
- job.extract, "Senior .NET Developer (Contractor)": salary_max: 1000000000000000 (esperado 45)
- job.extract, "Mid-level Software Engineer": currency: "BRL" (esperado "none")
- job.extract, "Líder Técnico de Desenvolvimento": contract: "contractor" (esperado "pj")
- job.extract, "QA Automation Engineer": seniority: "junior" (esperado "unknown"); contract: "clt" (esperado "unknown"); english: "none" (esperado "unknown")
- job.extract, "Senior Backend Engineer (Go)": contract: "clt" (esperado "unknown")
- email.classify, "Vaga .NET sênior em fintech": category: "interview_invite" (esperado "recruiter_outreach")

## Erros de qwen3.5:4b

- job.extract, "Desenvolvedor Backend .NET Pleno": period: "none" (esperado "month")
- job.extract, "Desenvolvedor Back-end": period: "none" (esperado "month")
- job.extract, "Desenvolvedora Python Pleno": period: "none" (esperado "month")
- job.extract, "QA Automation Engineer": english: "none" (esperado "unknown")
- email.classify, "Weekly digest: 10 new jobs for you": category: "application_received" (esperado "other")
- email.classify, "Opportunity at a US startup": action_needed: false (esperado true)

## Erros de qwen3.5:9b

- job.extract, "Tech Lead .NET": english: "none" (esperado "unknown")
- job.extract, "Estágio em Desenvolvimento de Software": english: "none" (esperado "unknown")
- job.extract, "Engenheiro de Software Pleno - Java": english: "none" (esperado "unknown")
- job.extract, "Desenvolvedor .NET Sênior": english: "none" (esperado "unknown")
- job.extract, "QA Automation Engineer": english: "none" (esperado "unknown")
- job.extract, "Desenvolvedor Júnior C#": english: "none" (esperado "unknown")

## Erros de gemma4:e4b

- job.extract, "Backend Developer": seniority: "mid" (esperado "unknown")
- job.extract, "Desenvolvedor Back-end": seniority: "mid" (esperado "unknown")
- job.extract, "QA Automation Engineer": seniority: "mid" (esperado "unknown")
