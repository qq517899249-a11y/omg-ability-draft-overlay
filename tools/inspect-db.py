import sqlite3
c=sqlite3.connect('research/dota_ad_data.db')
tables=[x[0] for x in c.execute("select name from sqlite_master where type='table' and name not like 'sqlite_%'")]
for t in tables:
    print(t,c.execute('select count(*) from '+t).fetchone()[0])
    print(c.execute('pragma table_info('+t+')').fetchall())
print('metadata',c.execute('select * from Metadata').fetchall())
