import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { Count } from '@jo/ui/count';
import { Icon } from '@jo/ui/icon';

interface Gpu { vendor: string; name: string; tempC: number | null; utilPct: number | null; vramUsedMb: number | null; vramTotalMb: number | null; powerW: number | null; powerLimitW: number | null; clockMhz: number | null; clockMaxMhz: number | null; memClockMhz: number | null; memClockMaxMhz: number | null; fanPct: number | null }
interface Cpu { name: string; tempC: number | null; hottestCoreC: number | null; utilPct: number | null; clockMhz: number | null; clockMaxMhz: number | null; threads: number }
interface Sample { at: string; gpus: Gpu[]; cpu: Cpu; ramUsedMb: number; ramTotalMb: number; fans: { label: string; rpm: number }[]; nvmeC: number | null; ramC: number | null }

const KEEP = 90;

/**
 * Sensores ao vivo (uma amostra por segundo, pelo SSE): temperatura com histórico de 90 s, uso, clocks, VRAM e energia da GPU,
 * temperatura, uso e clock da CPU, memória, ventoinhas, NVMe e RAM. A conexão fecha quando a tela sai.
 */
@Component({
  selector: 'app-sensors',
  imports: [Count, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header>
      <h2><span class="live" [class.on]="connected()" aria-hidden="true"></span>Sensores ao vivo</h2>
      <span class="faint small">{{ connected() ? 'uma leitura por segundo' : 'conectando…' }}</span>
    </header>
    @if (now(); as s) {
      <div class="cards">
        @for (g of s.gpus; track g.name; let gi = $index) {
          <section class="card">
            <div class="top">
              <span class="chip"><img [src]="g.vendor === 'nvidia' ? '/logos/nvidia.svg' : '/logos/amd.svg'" alt="" />{{ g.name.replace('NVIDIA GeForce ', '') }}</span>
              <span class="faint small">GPU</span>
            </div>
            <div class="temp" [class]="'temp ' + tone(g.tempC, 70, 84)">
              <span class="big num"><jo-count [value]="g.tempC ?? 0" /><small>°C</small></span>
              <svg viewBox="0 0 90 32" preserveAspectRatio="none" aria-hidden="true"><path [attr.d]="spark('gpu' + gi)" /></svg>
            </div>
            <dl>
              <div><dt>Uso</dt><dd><span class="bar"><span [style.width.%]="g.utilPct ?? 0"></span></span><span class="num">{{ g.utilPct ?? '—' }}%</span></dd></div>
              <div><dt>VRAM</dt><dd><span class="bar"><span [style.width.%]="pct(g.vramUsedMb, g.vramTotalMb)"></span></span><span class="num">{{ gb(g.vramUsedMb) }} / {{ gb(g.vramTotalMb) }} GB</span></dd></div>
              <div><dt>Clock</dt><dd><span class="bar"><span [style.width.%]="pct(g.clockMhz, g.clockMaxMhz)"></span></span><span class="num">{{ g.clockMhz ?? '—' }} / {{ g.clockMaxMhz ?? '—' }} MHz</span></dd></div>
              <div><dt>Memória</dt><dd><span class="bar"><span [style.width.%]="pct(g.memClockMhz, g.memClockMaxMhz)"></span></span><span class="num">{{ g.memClockMhz ?? '—' }} MHz</span></dd></div>
              <div><dt>Energia</dt><dd><span class="bar"><span [style.width.%]="pct(g.powerW, g.powerLimitW)"></span></span><span class="num">{{ g.powerW?.toFixed(0) ?? '—' }} / {{ g.powerLimitW?.toFixed(0) ?? '—' }} W</span></dd></div>
              @if (g.fanPct !== null) { <div><dt>Ventoinha</dt><dd><span class="bar"><span [style.width.%]="g.fanPct"></span></span><span class="num">{{ g.fanPct }}%</span></dd></div> }
            </dl>
          </section>
        }
        <section class="card">
          <div class="top">
            <span class="chip"><img [src]="s.cpu.name.includes('Intel') ? '/logos/intel.svg' : '/logos/amd.svg'" alt="" />{{ cpuName(s.cpu.name) }}</span>
            <span class="faint small">CPU · {{ s.cpu.threads }} threads</span>
          </div>
          <div class="temp" [class]="'temp ' + tone(s.cpu.tempC, 75, 90)">
            <span class="big num"><jo-count [value]="s.cpu.tempC ?? 0" /><small>°C</small></span>
            <svg viewBox="0 0 90 32" preserveAspectRatio="none" aria-hidden="true"><path [attr.d]="spark('cpu')" /></svg>
          </div>
          <dl>
            <div><dt>Uso</dt><dd><span class="bar"><span [style.width.%]="s.cpu.utilPct ?? 0"></span></span><span class="num">{{ s.cpu.utilPct?.toFixed(0) ?? '—' }}%</span></dd></div>
            <div><dt>Clock</dt><dd><span class="bar"><span [style.width.%]="pct(s.cpu.clockMhz, s.cpu.clockMaxMhz)"></span></span><span class="num">{{ s.cpu.clockMhz ?? '—' }} / {{ s.cpu.clockMaxMhz ?? '—' }} MHz</span></dd></div>
            <div><dt>Núcleo mais quente</dt><dd><span class="bar"><span [style.width.%]="s.cpu.hottestCoreC ?? 0"></span></span><span class="num">{{ s.cpu.hottestCoreC ?? '—' }} °C</span></dd></div>
            <div><dt>RAM</dt><dd><span class="bar"><span [style.width.%]="pct(s.ramUsedMb, s.ramTotalMb)"></span></span><span class="num">{{ gb(s.ramUsedMb) }} / {{ gb(s.ramTotalMb) }} GB</span></dd></div>
          </dl>
        </section>
        <section class="card">
          <div class="top"><span class="chip"><jo-icon name="fan" [size]="15" />Resfriamento e discos</span></div>
          <dl>
            @for (f of s.fans; track f.label) { <div><dt>{{ f.label }}</dt><dd><span class="bar"><span [style.width.%]="Math.min(100, f.rpm / 30)"></span></span><span class="num">{{ f.rpm }} rpm</span></dd></div> }
            @if (s.nvmeC !== null) { <div><dt>NVMe</dt><dd><span class="bar"><span [class]="tone(s.nvmeC, 60, 70)" [style.width.%]="s.nvmeC"></span></span><span class="num">{{ s.nvmeC.toFixed(0) }} °C</span></dd></div> }
            @if (s.ramC !== null) { <div><dt>Pentes de RAM</dt><dd><span class="bar"><span [style.width.%]="s.ramC"></span></span><span class="num">{{ s.ramC.toFixed(0) }} °C</span></dd></div> }
          </dl>
          <p class="faint small note">Com um modelo local respondendo, a GPU sobe de clock e de energia na hora. Acima de 84 °C na GPU ou 90 °C na CPU o número fica vermelho.</p>
        </section>
      </div>
    } @else {
      <p class="faint wait">Lendo os sensores…</p>
    }`,
  styles: `
    :host { display: block; }
    header { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 16px; border-bottom: 1px solid var(--line); }
    h2 { display: inline-flex; align-items: center; gap: 8px; font-size: 13px; }
    .live { width: 8px; height: 8px; border-radius: 50%; background: var(--text-3); }
    .live.on { background: var(--ok); animation: beat 1s ease-out infinite; }
    .small { font-size: 12px; }
    .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(280px, 100%), 1fr)); gap: 12px; padding: 14px 16px 16px; }
    .card { display: grid; gap: 10px; align-content: start; padding: 14px; border-radius: 6px; border: 1px solid var(--line); background: var(--surface-2); }
    .top { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
    .chip { display: inline-flex; align-items: center; gap: 7px; font: 600 12.5px var(--font-ui); min-width: 0; }
    .chip img { width: 18px; height: 18px; }
    .temp { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 12px; align-items: end; }
    .big { font-size: 34px; font-weight: 650; line-height: 1; letter-spacing: -0.02em; transition: color 400ms ease; }
    .big small { font-size: 14px; font-weight: 500; margin-left: 2px; color: var(--text-3); }
    svg { width: 100%; height: 34px; overflow: visible; }
    path { fill: none; stroke: currentColor; stroke-width: 1.6; vector-effect: non-scaling-stroke; stroke-linejoin: round; opacity: 0.85; }
    .temp.ok { color: var(--ok); } .temp.warn { color: var(--warn); } .temp.danger { color: var(--danger); }
    .temp.ok .big { color: var(--text); } .temp jo-count { color: inherit; }
    dl { display: grid; gap: 7px; margin: 0; }
    dl div { display: grid; grid-template-columns: 92px minmax(0, 1fr); gap: 10px; align-items: center; }
    dt { font-size: 12px; color: var(--text-3); }
    dd { margin: 0; display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 8px; align-items: center; font-size: 12px; }
    .bar { height: 5px; border-radius: 3px; background: var(--surface-3); overflow: hidden; }
    .bar span { display: block; height: 100%; background: var(--accent); transition: width 900ms cubic-bezier(0.2, 0.8, 0.2, 1); }
    .bar span.warn { background: var(--warn); } .bar span.danger { background: var(--danger); } .bar span.ok { background: var(--ok); }
    .num { font-family: var(--font-mono); font-variant-numeric: tabular-nums; color: var(--text-2); white-space: nowrap; }
    .note { margin: 4px 0 0; line-height: 1.45; }
    .wait { padding: 16px; margin: 0; }
    @keyframes beat { 0% { box-shadow: 0 0 0 0 color-mix(in oklch, var(--ok) 60%, transparent); } 100% { box-shadow: 0 0 0 7px transparent; } }
  `,
})
export class Sensors {
  protected readonly now = signal<Sample | null>(null);
  protected readonly connected = signal(false);
  private readonly history = signal<Record<string, number[]>>({});
  protected readonly Math = Math;

  constructor() {
    if (typeof EventSource === 'undefined') return;
    const es = new EventSource('/api/local/sensors/stream');
    es.addEventListener('open', () => this.connected.set(true));
    es.addEventListener('error', () => this.connected.set(false));
    es.addEventListener('message', (e) => {
      const s = JSON.parse((e as MessageEvent<string>).data) as Sample;
      this.now.set(s);
      this.history.update((h) => {
        const n = { ...h };
        const push = (k: string, v: number | null) => { if (v !== null) n[k] = [...(n[k] ?? []).slice(-(KEEP - 1)), v]; };
        s.gpus.forEach((g, i) => push('gpu' + i, g.tempC));
        push('cpu', s.cpu.tempC);
        return n;
      });
    });
    inject(DestroyRef).onDestroy(() => es.close());
  }

  /** Linha do histórico de temperatura, escalada entre 25 °C e 95 °C. */
  protected spark(key: string) {
    const v = this.history()[key] ?? [];
    if (v.length < 2) return '';
    return v.map((t, i) => `${i === 0 ? 'M' : 'L'} ${((i / (KEEP - 1)) * 90).toFixed(1)} ${(32 - ((Math.min(95, Math.max(25, t)) - 25) / 70) * 30).toFixed(1)}`).join(' ');
  }
  protected tone(t: number | null, warn: number, danger: number) { return t === null ? '' : t >= danger ? 'danger' : t >= warn ? 'warn' : 'ok'; }
  protected pct(a: number | null, b: number | null) { return a !== null && b ? Math.min(100, (a / b) * 100) : 0; }
  protected gb(mb: number | null) { return mb === null ? '—' : (mb / 1024).toLocaleString('pt-BR', { maximumFractionDigits: 1 }); }
  protected cpuName(n: string) { return n.replace(/\(R\)|\(TM\)|CPU|Processor|\d+-Core/gi, '').replace(/\s+/g, ' ').trim(); }
}
