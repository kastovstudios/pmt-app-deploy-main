/* Melhorias de navegação e acessibilidade sem alterar as regras de negócio. */
(() => {
    const page = document.body.dataset.page;
    const money = value => Number(value).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
    const normalize = text => text.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
    const labels = {
        telefone: 'Telefone com DDD', senha: 'Senha', nome: 'Nome de guerra', posto: 'Posto / graduação',
        novaSenha: 'Nova senha', confirmarNovaSenha: 'Confirme a nova senha',
        periodoDashboard: 'Mês de referência', periodoHistoricoAdmin: 'Mês de referência',
        filtroPostoHistorico: 'Posto / graduação', periodoCliente: 'Consultar mês',
        nomeProduto: 'Nome do produto', precoProduto: 'Preço (R$)',
        pesquisaClienteHistorico: 'Buscar cliente', pesquisaCliente: 'Buscar cliente',
        pesquisaClienteConfig: 'Buscar cliente', filtroPosto: 'Posto / graduação',
        filtroPostoConfig: 'Posto / graduação', configNome: 'Nome de guerra', configTelefone: 'Telefone com DDD'
    };
    Object.entries(labels).forEach(([id, text]) => {
        const input = document.getElementById(id);
        if (!input || document.querySelector(`label[for="${id}"]`)) return;
        const label = document.createElement('label');
        label.className = 'ui-label'; label.htmlFor = id; label.textContent = text;
        input.before(label);
    });

    document.querySelectorAll('input[type=password]').forEach(input => {
        const wrapper = document.createElement('div'); wrapper.className = 'ui-password';
        input.before(wrapper); wrapper.append(input);
        const toggle = document.createElement('button'); toggle.type = 'button';
        toggle.style.color = getComputedStyle(input).color;
        toggle.textContent = 'Mostrar'; toggle.setAttribute('aria-label', 'Mostrar senha');
        toggle.setAttribute('aria-pressed', 'false');
        toggle.addEventListener('click', () => {
            const show = input.type === 'password'; input.type = show ? 'text' : 'password';
            toggle.textContent = show ? 'Ocultar' : 'Mostrar';
            toggle.setAttribute('aria-label', show ? 'Ocultar senha' : 'Mostrar senha');
            toggle.setAttribute('aria-pressed', String(show));
        });
        wrapper.append(toggle);
    });

    document.querySelectorAll('form[data-action]').forEach(form => {
        const submit = form.querySelector('button[type=submit]');
        const error = document.createElement('p'); error.className = 'ui-form-error';
        error.setAttribute('role', 'alert'); error.hidden = true; form.append(error);
        let sending = false;
        form.addEventListener('submit', async event => {
            event.preventDefault();
            if (sending || !form.reportValidity()) return;
            sending = true; const text = submit.textContent;
            submit.disabled = true; submit.textContent = 'Aguarde…'; form.setAttribute('aria-busy', 'true');
            error.hidden = true;
            try { await window[form.dataset.action](); }
            catch { error.textContent = 'Não foi possível conectar. Confira sua conexão e tente novamente.'; error.hidden = false; }
            finally { sending = false; submit.disabled = false; submit.textContent = text; form.removeAttribute('aria-busy'); }
        });
    });

    if (page === 'admin') {
        const dashboard = document.getElementById('dashboard');
        const cards = [...dashboard.querySelectorAll(':scope > .card')];
        const metrics = document.createElement('div'); metrics.className = 'ui-metrics';
        if (cards.length) { cards[0].before(metrics); cards.forEach(card => metrics.append(card)); }
        const actions = document.createElement('div'); actions.className = 'ui-actions';
        dashboard.querySelectorAll(':scope > button').forEach(button => actions.append(button));
        dashboard.append(actions);
        const buttons = [...document.querySelectorAll('.sidebar button[onclick^="mostrar"]')];
        const original = window.mostrar;
        const activate = name => buttons.forEach(button => {
            const active = button.getAttribute('onclick').includes(`'${name}'`);
            if (active) button.setAttribute('aria-current', 'page'); else button.removeAttribute('aria-current');
        });
        window.mostrar = function(name) { activate(name); return original(name); };
        activate('dashboard');
        document.querySelector('.sidebar').setAttribute('aria-label', 'Navegação administrativa');
    }

    if (page === 'home') {
        const container = document.querySelector('.container');
        const menu = document.createElement('div'); menu.className = 'ui-home-menu';
        const descriptions = {
            Produtos: 'Escolha os itens e monte seu pedido', Historico: 'Consulte seus consumos e pagamentos',
            Histórico: 'Consulte seus consumos e pagamentos', Admin: 'Gerencie clientes, produtos e relatórios', Sair: 'Encerrar sua sessão com segurança'
        };
        container.querySelectorAll(':scope > button').forEach(button => {
            const name = button.textContent.trim();
            if (name === 'Historico') button.textContent = 'Histórico';
            const small = document.createElement('small'); small.textContent = descriptions[name] || '';
            button.append(small); menu.append(button);
        });
        container.append(menu);
    }

    if (page === 'produtos') {
        const grid = document.getElementById('gridProdutos');
        const label = document.createElement('label'); label.htmlFor = 'buscaProdutos'; label.className = 'ui-label'; label.textContent = 'Buscar no cardápio';
        const search = document.createElement('input'); search.id = 'buscaProdutos'; search.type = 'search'; search.placeholder = 'Digite o nome do produto';
        const empty = document.createElement('p'); empty.className = 'ui-empty'; empty.hidden = true; empty.textContent = 'Nenhum produto encontrado.'; empty.setAttribute('role', 'status');
        grid.before(label, search, empty);
        const filter = () => {
            const query = normalize(search.value.trim()); const cards = [...grid.querySelectorAll('.card')];
            cards.forEach(card => card.hidden = !normalize(card.querySelector('.nomeProduto').textContent).includes(query));
            empty.hidden = cards.some(card => !card.hidden);
        };
        search.addEventListener('input', filter);
        const finish = document.querySelector('.btn-finalizar'); let purchasing = false;
        const update = () => {
            let count = 0, total = 0;
            grid.querySelectorAll('.card').forEach(card => {
                const quantity = Number(card.querySelector('.controle span').textContent);
                count += quantity; total += quantity * Number(card.dataset.price);
                const minus = card.querySelector('.controle button'); minus.disabled = quantity === 0;
            });
            finish.disabled = purchasing || count === 0;
            finish.textContent = purchasing ? 'Registrando compra…' : count ? `Finalizar · ${count} ${count === 1 ? 'item' : 'itens'} · ${money(total)}` : 'Selecione os produtos';
        };
        const original = window.comprar;
        window.comprar = async function() {
            if (purchasing || finish.disabled) return;
            purchasing = true; update();
            try { await original(); }
            catch { alert('Não foi possível confirmar a compra. Confira seu histórico antes de tentar novamente.'); }
            finally { purchasing = false; update(); }
        };
        new MutationObserver(() => { filter(); update(); }).observe(grid, { childList: true, subtree: true, characterData: true });
        filter(); update();
    }
})();
