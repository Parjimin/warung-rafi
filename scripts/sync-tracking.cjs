const fs = require('node:fs');
module.exports = async ({ github, context, core }) => {
  const plan = JSON.parse(fs.readFileSync('.github/planning.json','utf8'));
  const repo = context.repo;
  const milestones = await github.paginate(github.rest.issues.listMilestones,{...repo,state:'all',per_page:100});
  const issues = await github.paginate(github.rest.issues.listForRepo,{...repo,state:'all',per_page:100});
  for (const item of plan.milestones) {
    let milestone = milestones.find(value => value.title === item.title);
    if (!milestone) milestone = (await github.rest.issues.createMilestone({...repo,title:item.title,description:item.description})).data;
    if (milestone.description !== item.description) {
      await github.rest.issues.updateMilestone({...repo,milestone_number:milestone.number,description:item.description});
    }
    for (const work of item.issues) {
      const marker = `<!-- warung-plan:${work.id} -->`;
      const existing = issues.find(issue => !issue.pull_request && issue.body?.includes(marker));
      if (existing) { core.info(`Preserve issue #${existing.number}: ${work.title}`); continue; }
      const body = `${marker}\n${work.body}\n\nKriteria penerimaan:\n${work.acceptance.map(text=>`- [ ] ${text}`).join('\n')}\n\nStatus dan bukti pengujian: [docs/STATUS.md](https://github.com/${repo.owner}/${repo.repo}/blob/main/docs/STATUS.md).\n\nIssue ditutup setelah kriteria diverifikasi; keberadaan kode saja belum berarti selesai.`;
      const created = await github.rest.issues.create({...repo,title:work.title,body,milestone:milestone.number});
      core.info(`Created issue #${created.data.number}`);
    }
  }
};
