(() => {
    let pagina = null, controller = null, quantidade = 0;
    const el = id => document.getElementById(id);
    const hoje = () => {
        const partes = new Intl.DateTimeFormat('en-CA', {timeZone: 'America/Sao_Paulo', year:'numeric', month:'2-digit', day:'2-digit'}).formatToParts(new Date());
        const valor = tipo => partes.find(p => p.type === tipo).value;
        return `${valor('year')}-${valor('month')}-${valor('day')}`;
    };
    async function carregar(mais = false) {
        controller?.abort();
        const atual = new AbortController(); controller = atual;
        const data = el('diaAtividades').value;
        el('erroAtividades').hidden = true;
        if (!data) { el('statusAtividades').textContent = 'Selecione um dia para consultar.'; el('listaAtividades').replaceChildren(); el('maisAtividades').hidden = true; return; }
        if (!mais) { pagina = null; quantidade = 0; el('listaAtividades').replaceChildren(); }
        el('maisAtividades').disabled = true;
        el('statusAtividades').textContent = 'Carregando atividades…';
        try {
            const query = new URLSearchParams({dia:data});
            if (mais && pagina) query.set('antes', pagina);
            const res = await fetch(`/admin/atividades?${query}`, {headers:{Authorization:localStorage.getItem('token') || ''}, signal:atual.signal});
            if (!res.ok) throw new Error(res.status === 401 ? 'Sua sessão expirou ou não tem acesso de administrador. Faça login novamente.' : 'Não foi possível carregar as atividades. Tente atualizar.');
            const dados = await res.json();
            for (const item of dados.itens) {
                const card = document.createElement('article'); card.className = 'atividade-card';
                const cabecalho = document.createElement('div'); cabecalho.className = 'atividade-cabecalho';
                const nome = document.createElement('strong'); nome.textContent = item.admin;
                const hora = document.createElement('time'); hora.dateTime = item.dataHora.replace(' ', 'T') + '-03:00'; hora.textContent = item.dataHora.slice(11);
                cabecalho.append(nome, hora);
                const acao = document.createElement('h3'); acao.textContent = item.acao;
                const detalhes = document.createElement('p'); detalhes.textContent = item.detalhes;
                const resultado = document.createElement('small'); resultado.textContent = item.resultado;
                card.append(cabecalho, acao, detalhes, resultado); el('listaAtividades').append(card);
            }
            quantidade += dados.itens.length; pagina = dados.proximaPagina;
            el('maisAtividades').hidden = !pagina;
            el('statusAtividades').textContent = quantidade ? `${quantidade} registro(s) carregado(s), do mais recente para o mais antigo.` : 'Nenhuma atividade registrada neste dia.';
        } catch (erro) {
            if (erro.name === 'AbortError') return;
            el('erroAtividades').textContent = erro.message; el('erroAtividades').hidden = false;
            el('statusAtividades').textContent = ''; el('maisAtividades').hidden = !pagina;
        } finally { if (controller === atual) el('maisAtividades').disabled = false; }
    }
    window.iniciarAtividades = () => { if (!el('diaAtividades').value) el('diaAtividades').value = hoje(); carregar(); };
    el('diaAtividades').addEventListener('change', () => carregar());
    el('hojeAtividades').addEventListener('click', () => { el('diaAtividades').value = hoje(); carregar(); });
    el('atualizarAtividades').addEventListener('click', () => carregar());
    el('maisAtividades').addEventListener('click', () => carregar(true));
})();
