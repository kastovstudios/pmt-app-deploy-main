(() => {
    const month = document.getElementById('mesComprasDiarias');
    const day = document.getElementById('diaComprasDiarias');
    const list = document.getElementById('listaComprasDiarias');
    const summary = document.getElementById('resumoComprasDiarias');
    const error = document.getElementById('erroComprasDiarias');
    const money = value => Number(value).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
    let initialized = false, requestId = 0;

    function today() {
        const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date());
        const get = type => parts.find(part => part.type === type).value;
        return `${get('year')}-${get('month')}-${get('day')}`;
    }
    function days(selected) {
        day.replaceChildren();
        if (!/^\d{4}-\d{2}$/.test(month.value)) return;
        const [year, number] = month.value.split('-').map(Number);
        const count = new Date(year, number, 0).getDate();
        for (let d = 1; d <= count; d++) {
            const option = document.createElement('option');
            option.value = String(d).padStart(2, '0'); option.textContent = String(d).padStart(2, '0'); day.append(option);
        }
        day.value = String(Math.min(Math.max(Number(selected) || 1, 1), count)).padStart(2, '0');
    }
    function selectToday() { const date = today(); month.value = date.slice(0, 7); days(date.slice(8)); }
    function element(tag, text, className) {
        const el = document.createElement(tag); if (text !== undefined) el.textContent = text;
        if (className) el.className = className; return el;
    }

    async function action(compra, type, button, card) {
        const pago = type === 'pagar';
        const text = type === 'remover'
            ? `Remover a compra de ${compra.nome} de ${money(compra.total)}? Todos os itens desta compra serão removidos e devolvidos ao estoque.`
            : `${pago ? 'Marcar somente esta compra como paga' : 'Desfazer o pagamento desta compra'} para ${compra.nome} (${money(compra.total)})?`;
        if (!confirm(text)) return;
        const buttons = card.querySelectorAll('button'); buttons.forEach(b => b.disabled = true);
        const label = button.textContent; button.textContent = 'Aguarde…'; error.hidden = true;
        try {
            const base = `/admin/compras-diarias/${compra.tipo}/${compra.id}`;
            const res = await fetch(base + (type === 'remover' ? '' : '/pagamento'), {
                method: type === 'remover' ? 'DELETE' : 'PUT',
                headers: { Authorization: localStorage.getItem('token'), 'Content-Type': 'application/json' },
                ...(type === 'remover' ? {} : { body: JSON.stringify({ pago, totalEsperado: compra.total }) })
            });
            if (!res.ok) throw new Error(await res.text() || 'Não foi possível atualizar a compra.');
            await load();
        } catch (err) { error.textContent = err.message || 'Erro de conexão. Atualize a lista antes de tentar novamente.'; error.hidden = false; }
        finally { buttons.forEach(b => b.disabled = false); button.textContent = label; }
    }

    async function load() {
        const current = ++requestId;
        list.replaceChildren(); error.hidden = true;
        if (!month.value || !day.value) { summary.textContent = 'Selecione o mês e o dia.'; return; }
        const date = `${month.value}-${day.value}`;
        summary.textContent = 'Carregando compras…'; list.setAttribute('aria-busy', 'true');
        try {
            const res = await fetch(`/admin/compras-diarias?dia=${encodeURIComponent(date)}`, { headers: { Authorization: localStorage.getItem('token') } });
            if (!res.ok) throw new Error(await res.text() || 'Não foi possível carregar as compras.');
            const data = await res.json(); if (current !== requestId) return;
            const total = data.compras.reduce((sum, c) => sum + c.total, 0);
            summary.textContent = `${date.split('-').reverse().join('/')} · ${data.compras.length} compra(s) · ${money(total)}`;
            if (!data.compras.length) { list.append(element('p', 'Nenhuma compra registrada neste dia.', 'diario-vazio')); return; }
            for (const compra of data.compras) {
                const card = element('article', undefined, 'diario-compra');
                const header = element('div', undefined, 'diario-cabecalho');
                const name = element('div'); name.append(element('h3', `${compra.posto} ${compra.nome}`.trim()));
                name.append(element('small', `${compra.data.slice(11, 16)} · ${compra.tipo === 'compra' ? 'Compra' : 'Lançamento'} #${compra.id}`));
                const status = element('strong', `${money(compra.total)}${compra.pago ? ' - Pago' : compra.valorPago > 0 ? ' - Parcial' : ' - Pendente'}`);
                if (compra.pago) status.classList.add('status-pago'); header.append(name, status);
                const items = element('ul'); compra.itens.forEach(item => items.append(element('li', `${item.quantidade}× ${item.produto} · ${money(item.total)}`)));
                const controls = element('div', undefined, 'diario-acoes');
                const add = (text, type) => { const btn = element('button', text); btn.type = 'button'; btn.addEventListener('click', () => action(compra, type, btn, card)); controls.append(btn); return btn; };
                if (!compra.pago) add('Marcar compra paga', 'pagar').disabled = compra.total <= 0;
                if (compra.valorPago > 0) add('Desfazer pago', 'desfazer');
                add('Remover compra', 'remover'); card.append(header, items, controls); list.append(card);
            }
        } catch (err) {
            if (current !== requestId) return;
            summary.textContent = ''; error.textContent = err.message || 'Erro de conexão ao carregar compras.'; error.hidden = false;
        } finally { if (current === requestId) list.removeAttribute('aria-busy'); }
    }

    window.iniciarComprasDiarias = () => { if (!initialized) { selectToday(); initialized = true; } return load(); };
    month.addEventListener('change', () => { days(day.value); load(); });
    day.addEventListener('change', load);
    document.getElementById('hojeComprasDiarias').addEventListener('click', () => { selectToday(); load(); });
    document.getElementById('atualizarComprasDiarias').addEventListener('click', load);
})();
